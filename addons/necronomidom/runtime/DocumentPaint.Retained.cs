#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentPaint
{
    // Slot is a MultiMesh instance, owned _items text, or glyph/background/border-store index; other contexts use -1.
    // StyleRow is a box instance's custom-data style row, kept here so retained updates never read the
    // MultiMesh back from the RenderingServer (that read synchronizes with the render thread every call).
    private readonly record struct RetainedBinding(PaintRun Run, CanvasItem? Item, int Slot, int StyleRow = 0);
    private readonly List<RetainedBinding> _retainedBindings = new();
    private readonly HashSet<MultiMeshInstance2D> _changedBatches = new();
    private readonly HashSet<PolygonComposite> _dirtyTargets = new();
    private void PrepareRetainedBindings(DisplayList list)
    {
        foreach (var run in list.Runs) _retainedBindings.Add(new(run, null, -1));
    }
    private void BindRetained(int index, CanvasItem item, int slot = -1, int styleRow = 0) =>
        _retainedBindings[index] = _retainedBindings[index] with { Item = item, Slot = slot, StyleRow = styleRow };
    public void UpdateTransform(int ordinal, Affine2D transform)
    {
        int current = 0;
        for (int i = 0; i < _retainedBindings.Count; i++)
        {
            var binding = _retainedBindings[i];
            if (binding.Run is not BeginTransformRun run) continue;
            if (current++ != ordinal) continue;
            if (binding.Item is not Node2D item) throw new InvalidOperationException("Missing retained transform carrier.");
            item.Transform = BorderPaintStore.ToNative(transform);
            _retainedBindings[i] = binding with { Run = run with { Transform = transform } };
            _dirtyTargets.Clear();
            MarkRetainedContentChanged(item, false);
            FitRetainedTargets();
            return;
        }
        throw new ArgumentOutOfRangeException(nameof(ordinal));
    }
    private void FitRetainedTargets()
    {
        foreach (var context in _isolatedContexts) context.BeginRebuild();
        foreach (var context in _isolatedContexts) context.MeasurePaint();
        foreach (var context in _isolatedContexts) context.FitTarget(_dirtyTargets.Contains(context));
        _dirtyTargets.Clear();
    }
    private void MarkRetainedContentChanged(CanvasItem? item, bool ownContent)
    {
        if (item == null) return;
        if (ownContent && item is PolygonComposite composite) _dirtyTargets.Add(composite);
        Viewport parent = item.GetViewport();
        while (parent is SubViewport && parent.GetParent() is PolygonComposite owner)
        {
            _dirtyTargets.Add(owner);
            parent = owner.GetViewport();
        }
    }

    /// <summary>Validate the entire topology before touching native state. Unchanged glyph/mask/image
    /// nodes and buffers stay borrowed and attached; only actual run deltas reach the compositor.</summary>
    internal bool TryUpdateRetained(DisplayList list, INativeFontFamilies fonts, NativeImageSource images)
    {
        if (_retainedBindings.Count == 0 || list.Runs.Count != _retainedBindings.Count) return false;
        for (int i = 0; i < list.Runs.Count; i++)
        {
            var before = _retainedBindings[i]; var next = list.Runs[i];
            if (before.Run.GetType() != next.GetType()) return false;
            if (next is BeginPolygonClipRun)
            {
                bool single = before.Item != null && TrySinglePolygon(list, i, before.Item, out _);
                if (single != (before.Item is PolygonPaint { IsSingleFill: true })) return false;
                if (single) { i += 2; continue; }
            }
            if (Equals(before.Run, next)) continue;
            bool supported = (next, before.Item) switch {
                (PolygonRun, PolygonPaint) => true,
                (BeginPolygonClipRun, PolygonClipGroup) => true,
                (BeginRectClipRun, RoundedRectClipGroup) or (BeginRoundedRectClipRun, RoundedRectClipGroup) => true,
                (BeginEllipticalClipRun, EllipticalClipGroup) => true,
                (BackgroundImageRun, EllipticalClipGroup) => _backgrounds?.Owns(before.Slot, before.Item) == true,
                (BorderBoxRun, MeshInstance2D) => _borders?.Owns(before.Slot, before.Item) == true,
                (BeginOpacityRun, PolygonOpacityGroup) => true,
                (BeginTransformRun, Node2D) => true,
                (BoxRun, MultiMeshInstance2D) => (uint)before.StyleRow < (uint)_styleRowCount,
                (TextEffectsRun, NativeGlyphEffectsPaint) => _glyphs.Owns(before.Slot, before.Item),
                (TextRun, ShapedTextPaint) =>
                    (uint)before.Slot < (uint)_items.Count && ReferenceEquals(_items[before.Slot], before.Item),
                (EndTransformRun, null) or (EndEllipticalClipRun, null) => true,
                _ => false
            };
            if (!supported) return false;
        }
        bool changed = false, stylesChanged = false;
        _changedBatches.Clear();
        _dirtyTargets.Clear();
        for (int i = 0; i < list.Runs.Count; i++)
        {
            var binding = _retainedBindings[i]; var next = list.Runs[i];
            if (binding.Item is PolygonPaint { IsSingleFill: true } single && next is BeginPolygonClipRun)
            {
                var fill = list.Runs[i + 1];
                if (!Equals(binding.Run, next) || !Equals(_retainedBindings[i + 1].Run, fill))
                {
                    changed = true;
                    MarkRetainedContentChanged(single, false);
                    single.Update((PolygonRun)fill);
                    _retainedBindings[i] = binding with { Run = next };
                    _retainedBindings[i + 1] = _retainedBindings[i + 1] with { Run = fill };
                }
                i += 2; continue;
            }
            if (Equals(binding.Run, next)) continue;
            changed = true;
            // Image slots change captured child pixels; clip/opacity proxies change only their ancestors.
            MarkRetainedContentChanged(binding.Item, next is BackgroundImageRun);
            switch (next, binding.Item)
            {
                case (PolygonRun run, PolygonPaint item): item.Update(run); break;
                case (BeginPolygonClipRun run, PolygonClipGroup item): item.Update(run); break;
                case (BeginRectClipRun run, RoundedRectClipGroup item): item.Update(run.Bounds, 0); break;
                case (BeginRoundedRectClipRun run, RoundedRectClipGroup item): item.Update(run.Bounds, run.Radius); break;
                case (BeginEllipticalClipRun run, EllipticalClipGroup item): item.Update(run); break;
                case (BackgroundImageRun run, EllipticalClipGroup): _backgrounds!.Update(binding.Slot, run, images); break;
                case (BorderBoxRun run, MeshInstance2D): _borders!.Update(binding.Slot, run); break;
                case (BeginOpacityRun run, PolygonOpacityGroup item): item.Update(run.Opacity, false); break;
                case (BeginTransformRun run, Node2D item): item.Transform = BorderPaintStore.ToNative(run.Transform); break;
                case (TextRun, ShapedTextPaint):
                case (TextEffectsRun, NativeGlyphEffectsPaint):
                    binding = ReplaceRetainedText(binding, next, fonts); break;
                case (BoxRun run, MultiMeshInstance2D item):
                    var prior = (BoxRun)binding.Run;
                    if (prior.Style != run.Style) { WriteBoxStyle(binding.StyleRow, run.Style); stylesChanged = true; }
                    if (prior.Bounds != run.Bounds)
                    {
                        var b = run.Bounds;
                        var rect = new Rect2((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height);
                        item.Multimesh.SetInstanceTransform2D(binding.Slot, new Transform2D(
                            new Vector2((float)b.Width, 0), new Vector2(0, (float)b.Height), rect.GetCenter()));
                        item.Multimesh.SetInstanceCustomData(binding.Slot, BoxCustomData(binding.StyleRow, b.Width, b.Height));
                        _changedBatches.Add(item);
                    }
                    break;
            }
            _retainedBindings[i] = binding with { Run = next };
        }
        foreach (var item in _changedBatches)
        {
            bool first = true; Rect2 bounds = default;
            foreach (var binding in _retainedBindings)
                if (ReferenceEquals(binding.Item, item) && binding.Run is BoxRun box)
                {
                    var b = box.Bounds;
                    var rect = new Rect2((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height);
                    bounds = first ? rect : bounds.Merge(rect); first = false;
                }
            item.Multimesh.CustomAabb = new Aabb(new Vector3(bounds.Position.X, bounds.Position.Y, 0),
                new Vector3(bounds.Size.X, bounds.Size.Y, 0));
        }
        _changedBatches.Clear();
        if (stylesChanged) _styleTexture!.Update(_stylePixels!);
        if (changed) FitRetainedTargets();
        return true;
    }
    private RetainedBinding ReplaceRetainedText(RetainedBinding binding, PaintRun run, INativeFontFamilies fonts)
    {
        if (run is TextEffectsRun effects && binding.Item is NativeGlyphEffectsPaint)
        {
            _glyphs.Update(binding.Slot, effects, fonts);
            return binding;
        }
        Node2D replacement = run switch
        {
            TextRun text => new ShapedTextPaint(fonts.Resolve(text), text),
            _ => throw new InvalidOperationException("Retained glyph replacement requires a text run.")
        };
        var previous = (Node2D)binding.Item!;
        Node parent = previous.GetParent();
        int sibling = previous.GetIndex();
        parent.AddChild(replacement); parent.MoveChild(replacement, sibling);
        parent.RemoveChild(previous); previous.Free();
        _items[binding.Slot] = replacement;
        return binding with { Item = replacement };
    }
}
