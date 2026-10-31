#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentPaint
{
    // Slot is a MultiMesh instance index, or the owned _items index for a glyph node; store-owned contexts use -1.
    private readonly record struct RetainedBinding(PaintRun Run, CanvasItem? Item, int Slot);
    private readonly List<RetainedBinding> _retainedBindings = new();
    private readonly HashSet<MultiMeshInstance2D> _changedBatches = new();
    private void PrepareRetainedBindings(DisplayList list)
    {
        foreach (var run in list.Runs) _retainedBindings.Add(new(run, null, -1));
    }
    private void BindRetained(int index, CanvasItem item, int slot = -1) =>
        _retainedBindings[index] = _retainedBindings[index] with { Item = item, Slot = slot };
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
            FitRetainedTargets();
            return;
        }
        throw new ArgumentOutOfRangeException(nameof(ordinal));
    }
    private void FitRetainedTargets()
    {
        foreach (var context in _isolatedContexts) context.BeginRebuild();
        foreach (var context in _isolatedContexts) context.MeasurePaint();
        foreach (var context in _isolatedContexts) context.FitTarget();
    }

    /// <summary>Validate the entire topology before touching native state. Unchanged glyph/mask/image
    /// nodes and buffers stay borrowed and attached; only actual run deltas reach the compositor.</summary>
    private bool TryUpdateRetained(DisplayList list, INativeFontFamilies fonts)
    {
        if (_retainedBindings.Count == 0 || list.Runs.Count != _retainedBindings.Count) return false;
        for (int i = 0; i < list.Runs.Count; i++)
        {
            var before = _retainedBindings[i]; var next = list.Runs[i];
            if (Equals(before.Run, next)) continue;
            if (before.Run.GetType() != next.GetType()) return false;
            bool supported = (next, before.Item) switch {
                (PolygonRun, PolygonPaint) => true,
                (BeginPolygonClipRun, PolygonClipGroup) => true,
                (BeginOpacityRun, PolygonOpacityGroup) => true,
                (BeginTransformRun, Node2D) => true,
                (BoxRun box, MultiMeshInstance2D) => before.Run is BoxRun prior && box.Style == prior.Style,
                (TextRun, ShapedTextPaint) or (TextEffectsRun, NativeGlyphEffectsPaint) =>
                    (uint)before.Slot < (uint)_items.Count && ReferenceEquals(_items[before.Slot], before.Item),
                (EndTransformRun, null) => true,
                _ => false
            };
            if (!supported) return false;
        }
        bool changed = false;
        _changedBatches.Clear();
        for (int i = 0; i < list.Runs.Count; i++)
        {
            var binding = _retainedBindings[i]; var next = list.Runs[i];
            if (Equals(binding.Run, next)) continue;
            changed = true;
            switch (next, binding.Item)
            {
                case (PolygonRun run, PolygonPaint item): item.Update(run); break;
                case (BeginPolygonClipRun run, PolygonClipGroup item): item.Update(run); break;
                case (BeginOpacityRun run, PolygonOpacityGroup item): item.Update(run.Opacity); break;
                case (BeginTransformRun run, Node2D item): item.Transform = BorderPaintStore.ToNative(run.Transform); break;
                case (TextRun, ShapedTextPaint):
                case (TextEffectsRun, NativeGlyphEffectsPaint):
                    binding = ReplaceRetainedText(binding, next, fonts); break;
                case (BoxRun run, MultiMeshInstance2D item):
                    var b = run.Bounds;
                    var rect = new Rect2((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height);
                    item.Multimesh.SetInstanceTransform2D(binding.Slot, new Transform2D(
                        new Vector2((float)b.Width, 0), new Vector2(0, (float)b.Height), rect.GetCenter()));
                    var custom = item.Multimesh.GetInstanceCustomData(binding.Slot);
                    custom.B = (float)b.Width; custom.A = (float)b.Height;
                    item.Multimesh.SetInstanceCustomData(binding.Slot, custom);
                    _changedBatches.Add(item); break;
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
        if (changed) FitRetainedTargets();
        return true;
    }
    private RetainedBinding ReplaceRetainedText(RetainedBinding binding, PaintRun run, INativeFontFamilies fonts)
    {
        Node2D replacement = run switch
        {
            TextRun text => new ShapedTextPaint(fonts.Resolve(text), text),
            TextEffectsRun effects => new NativeGlyphEffectsPaint(effects, fonts.Resolve(effects.Text).GetShapedRun(effects.Text)),
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
