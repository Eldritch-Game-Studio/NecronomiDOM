using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>A prepared paint-only replacement; no flow boxes or containing blocks change.</summary>
public readonly record struct LeafPolygonPaintUpdate(Element Element, int RunIndex, int BoxIndex,
    BeginPolygonClipRun Clip, PolygonRun Fill, CssValue ClipValue);

public sealed partial class FlowLayout
{
    private readonly Dictionary<PaintRun, Element> _polygonPaintOwners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Element, PolygonPaintSlot> _polygonPaintSlots = new();
    private readonly record struct PolygonPaintSlot(int RunIndex, int BoxIndex, CssValue? ClipValue);

    private void ResetPolygonPaint()
    {
        _polygonPaintOwners.Clear();
        _polygonPaintSlots.Clear();
    }

    private void CapturePolygonPaintSlots()
    {
        if (_polygonPaintOwners.Count == 0) return;
        for (int i = 0; i + 2 < _result.Runs.Count; i++)
        {
            if (!_polygonPaintOwners.TryGetValue(_result.Runs[i], out var element) ||
                _result.Runs[i] is not BeginPolygonClipRun clip ||
                _result.Runs[i + 1] is not PolygonRun fill || _result.Runs[i + 2] is not EndPolygonClipRun ||
                clip.Bounds != fill.Bounds || !clip.Geometry.Equals(fill.Geometry)) continue;
            // Fragmented or multiply painted elements retain the full layout path.
            _polygonPaintSlots[element] = _polygonPaintSlots.ContainsKey(element)
                ? new(-1, -2, null) : new(i, -1, null);
        }
        for (int i = 0; i < _result.Boxes.Count; i++)
        {
            var element = _result.Boxes[i].Element;
            if (!_polygonPaintSlots.TryGetValue(element, out var slot) || slot.RunIndex < 0) continue;
            _polygonPaintSlots[element] = slot.BoxIndex == -1
                ? slot with { BoxIndex = i, ClipValue = ComputedValue(_styles.GetComputedStyle(element), "clip-path") }
                : slot with { BoxIndex = -2 };
        }
        _polygonPaintOwners.Clear();
    }

    /// <summary>The caller first consumes an independent paint-only delta from StyleEngine. Only an
    /// existing childless polygon-clip/fill/end span is admitted; other decorations keep full layout.</summary>
    public bool TryPreparePolygonPaintUpdate(Element element, out LeafPolygonPaintUpdate update)
    {
        update = default;
        if (element.FirstChild != null || !_polygonPaintSlots.TryGetValue(element, out var slot) ||
            slot.RunIndex < 0 || slot.BoxIndex < 0 || slot.ClipValue == null) return false;
        var box = _result.Boxes[slot.BoxIndex];
        if (box.UsedGeometry is not { Polygon: { } previousPolygon } ||
            _result.Runs[slot.RunIndex] is not BeginPolygonClipRun clip ||
            _result.Runs[slot.RunIndex + 1] is not PolygonRun fill ||
            _result.Runs[slot.RunIndex + 2] is not EndPolygonClipRun) return false;
        var style = _styles.GetComputedStyle(element);
        var value = ComputedValue(style, "clip-path");
        var polygon = ReferenceEquals(value, slot.ClipValue) ? previousPolygon : PolygonClipResolver.Resolve(value, box.Bounds);
        var color = SampleBackgroundColor(element, Color(style, "background-color"));
        if (color.A <= 0) return false;
        update = new(element, slot.RunIndex, slot.BoxIndex,
            ReferenceEquals(polygon, previousPolygon) ? clip : new BeginPolygonClipRun(box.Bounds, polygon),
            ReferenceEquals(polygon, previousPolygon) && color == fill.Color ? fill : new PolygonRun(box.Bounds, polygon, color), value);
        return true;
    }

    /// <summary>Commit a prepared batch and refresh only changed leaf hit masks, never flex/text flow.
    /// Native paint must independently admit the existing retained topology or request full layout.</summary>
    public bool ApplyPolygonPaintUpdates(ReadOnlySpan<LeafPolygonPaintUpdate> updates)
    {
        foreach (ref readonly var update in updates)
            if (!_polygonPaintSlots.TryGetValue(update.Element, out var slot) ||
                slot.RunIndex != update.RunIndex || slot.BoxIndex != update.BoxIndex)
                throw new ArgumentException("Polygon paint update does not match the retained slots.", nameof(updates));
        bool geometryChanged = false;
        foreach (ref readonly var update in updates)
        {
            var box = _result.Boxes[update.BoxIndex];
            var geometry = box.UsedGeometry ?? throw new InvalidOperationException("Retained polygon lost its used geometry.");
            _result.Runs[update.RunIndex] = update.Clip;
            _result.Runs[update.RunIndex + 1] = update.Fill;
            if (!ReferenceEquals(geometry.Polygon, update.Clip.Geometry))
            {
                box.UsedGeometry = geometry with { Polygon = update.Clip.Geometry };
                CompleteHitGeometryAt(update.BoxIndex);
                geometryChanged = true;
            }
            _polygonPaintSlots[update.Element] = new(update.RunIndex, update.BoxIndex, update.ClipValue);
        }
        return geometryChanged;
    }
}
