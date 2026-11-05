#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    private readonly HashSet<Element> _inlineStyleChanges = new();
    private readonly List<(Element Element, int Ordinal, double Alpha)> _inlineOpacityUpdates = new();
    private readonly List<(Element Element, int Ordinal, UsedBoxTransform Transform)> _inlineTransformUpdates = new();
    private readonly List<LeafPolygonPaintUpdate> _inlinePolygonPaintUpdates = new();
    public int GeometryRevision { get; private set; }

    private bool TryUpdateInlineCompositor()
    {
        if (_invalidation!.StyleSheetsChanged() || Styles.Media.Width != Size.X || Styles.Media.Height != Size.Y)
            return false;
        _inlineOpacityUpdates.Clear(); _inlineTransformUpdates.Clear(); _inlinePolygonPaintUpdates.Clear();
        try
        {
            foreach (var element in _inlineStyleChanges)
            {
                if (!element.IsConnected) return false;
                if (Styles.TryConsumeInlineTransformChange(element))
                {
                    var target = ScaleTransitionStyle.Resolve(Styles.GetComputedValues(element));
                    if (_scaleController.Contains(element) || target.Enabled && (target.Duration > 0 || target.Delay > 0) ||
                        !_layout!.TryPrepareTransformUpdate(element, out int ordinal, out var transform)) return false;
                    _inlineTransformUpdates.Add((element, ordinal, transform));
                    continue;
                }
                if (Styles.TryConsumeInlinePolygonPaintChange(element))
                {
                    var target = BackgroundColorTransitionStyle.Resolve(Styles.GetComputedStyle(element));
                    if (_backgroundColorController.TryGetBackgroundColor(element, out _) ||
                        target.Enabled && (target.Duration > 0 || target.Delay > 0) ||
                        !_layout!.TryPreparePolygonPaintUpdate(element, out var update)) return false;
                    _inlinePolygonPaintUpdates.Add(update);
                    continue;
                }
                if (!Styles.TryConsumeInlineOpacityChange(element)) return false;
                var style = Styles.GetComputedStyle(element);
                var opacity = OpacityTransitionStyle.Resolve(style);
                if (_opacityController.TryGetOpacity(element, out _) ||
                    opacity.Enabled && (opacity.Duration > 0 || opacity.Delay > 0)) return false;
                int group = -1;
                foreach (var binding in _opacityGroups)
                    if (ReferenceEquals(binding.Element, element)) { group = binding.Ordinal; break; }
                if (group < 0) return false;
                if (opacity.Opacity >= 1)
                {
                    bool independentPhase = false;
                    foreach (var box in DisplayList.Boxes)
                        if (ReferenceEquals(box.Element, element))
                        {
                            independentPhase = BoxTransformResolver.Resolve(Styles.GetComputedValues(element), box.Bounds)
                                .EstablishesContainingBlock || style.GetPropertyValue("position") != "static" ||
                                style.GetPropertyValue("clip-path") != "none";
                            break;
                        }
                    if (!independentPhase) return false;
                }
                _inlineOpacityUpdates.Add((element, group, opacity.Opacity));
            }
            if (_inlinePolygonPaintUpdates.Count != 0)
            {
                // Text-control selection/caret runs are a separate projection of the layout list.
                // Keep their existing full update path rather than overwrite that borrowed paint.
                foreach (var run in DisplayList.Runs)
                    if (run is TextRun { TextControl: not null } or
                        TextEffectsRun { Text: { TextControl: not null } }) return false;
            }
            foreach (var update in _inlineTransformUpdates)
                _layout!.ApplyTransformUpdate(update.Element, update.Transform);
            bool clipChanged = false;
            if (_inlinePolygonPaintUpdates.Count != 0)
            {
                clipChanged = _layout!.ApplyPolygonPaintUpdates(CollectionsMarshal.AsSpan(_inlinePolygonPaintUpdates));
                // Validate every native binding before touching it. An incompatible retained
                // carrier falls back to the complete layout/paint path in this same call.
                if (!_paint.TryUpdateRetained(DisplayList, _shaper!, _images!)) return false;
                foreach (var update in _inlinePolygonPaintUpdates)
                    _backgroundColorBefore[update.Element] = update.Fill.Color;
            }
            else
                foreach (var update in _inlineTransformUpdates)
                    _paint.UpdateTransform(update.Ordinal, update.Transform.Matrix);
            foreach (var update in _inlineOpacityUpdates)
            {
                _paint.UpdateOpacity(update.Ordinal, (float)update.Alpha);
                _opacityBefore[update.Element] = update.Alpha;
            }
            if (_inlineTransformUpdates.Count != 0 || clipChanged)
            {
                GeometryRevision++;
                _documentInput!.UpdateGeometry(DisplayList, Size.X, Size.Y);
            }
            return true;
        }
        finally
        {
            _inlineOpacityUpdates.Clear(); _inlineTransformUpdates.Clear(); _inlinePolygonPaintUpdates.Clear();
        }
    }
}
