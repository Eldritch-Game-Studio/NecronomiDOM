using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>A guarded position-only change against one completed layout generation.</summary>
public readonly record struct AbsolutePositionUpdate(Element Element, int Generation, double X, double Y);

public sealed partial class FlowLayout
{
    private sealed class AbsolutePositionSlot
    {
        internal Element Element = null!;
        internal PositionedContainingBlock Flow, Absolute;
        internal double StaticX, StaticY, NaturalHeight, Width, Height;
        internal bool HeightDefinite, Valid;
        internal PaintRun? First, Last;
        internal int FenceCount;
        internal int Start = -1, End = -1, FirstCount, LastCount, Root = -1, BoxCount;
        internal int[] Boxes = Array.Empty<int>(), Fences = Array.Empty<int>();
    }
    private readonly Dictionary<Element, AbsolutePositionSlot> _absolutePositions = new();
    private readonly Dictionary<Element, int> _absoluteBoxIndices = new();
    private readonly Dictionary<PaintRun, List<AbsolutePositionSlot>> _absoluteAnchors = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> _absoluteHitUpdates = new();
    private int _absoluteGeneration;

    private void ResetAbsolutePositions()
    {
        _absoluteGeneration++;
        _absolutePositions.Clear(); _absoluteAnchors.Clear(); _absoluteHitUpdates.Clear(); _absoluteBoxIndices.Clear();
    }

    private void CaptureAbsolutePosition(Element element, CSSStyleDeclaration style, PositionedBoxPlan plan,
        PositionedContainingBlock flow, PositionedContainingBlock absolute, double x, double y,
        double natural, int boxStart, int runStart)
    {
        if (plan.Mode != PositionedMode.Absolute || element is not HTMLElement html ||
            style.GetPropertyValue("width") == "auto" || SampleWidth(element).HasValue ||
            style.GetPropertyValue("overflow-x") is "auto" or "scroll" ||
            style.GetPropertyValue("overflow-y") is "auto" or "scroll") return;
        bool inlineInset = false;
        foreach (var declaration in html.Style.Declarations)
            if (declaration.Name is "left" or "right" or "top" or "bottom") { inlineInset = true; break; }
        if (!inlineInset) return;
        var bounds = _result.Boxes[boxStart].Bounds;
        var slot = new AbsolutePositionSlot
        {
            Element = element, Flow = flow, Absolute = absolute, StaticX = x, StaticY = y,
            NaturalHeight = natural, Width = bounds.Width, Height = bounds.Height,
            HeightDefinite = plan.DefiniteContentHeight.HasValue,
            Boxes = new int[_result.Boxes.Count - boxStart]
        };
        _absolutePositions.Add(element, slot);
        if (runStart == _result.Runs.Count) return;
        slot.First = _result.Runs[runStart]; slot.Last = _result.Runs[^1];
        AddAbsoluteAnchor(slot.First, slot);
        if (!ReferenceEquals(slot.First, slot.Last)) AddAbsoluteAnchor(slot.Last, slot);
    }

    private void AddAbsoluteAnchor(PaintRun run, AbsolutePositionSlot slot)
    {
        if (!_absoluteAnchors.TryGetValue(run, out var slots)) _absoluteAnchors.Add(run, slots = new());
        slots.Add(slot);
    }

    private void TranslateAbsoluteAnchor(PaintRun before, PaintRun after)
    {
        if (_absoluteAnchors.Count == 0 || !_absoluteAnchors.Remove(before, out var slots)) return;
        foreach (var slot in slots)
        {
            if (ReferenceEquals(slot.First, before)) slot.First = after;
            if (ReferenceEquals(slot.Last, before)) slot.Last = after;
        }
        _absoluteAnchors.Add(after, slots);
    }

    private void CaptureAbsolutePositionIndices()
    {
        if (_absolutePositions.Count == 0) return;
        for (int i = 0; i < _result.Runs.Count; i++)
            if (_absoluteAnchors.TryGetValue(_result.Runs[i], out var slots))
                foreach (var slot in slots)
                {
                    if (ReferenceEquals(slot.First, _result.Runs[i])) { slot.Start = i; slot.FirstCount++; }
                    if (ReferenceEquals(slot.Last, _result.Runs[i])) { slot.End = i; slot.LastCount++; }
                }
        for (int i = 0; i < _result.Boxes.Count; i++) _absoluteBoxIndices.TryAdd(_result.Boxes[i].Element, i);
        for (int i = 0; i < _result.Boxes.Count; i++)
            for (var ancestor = _result.Boxes[i].Element; ancestor != null; ancestor = ancestor.ParentElement)
                if (_absolutePositions.TryGetValue(ancestor, out var slot))
                {
                    if (slot.BoxCount < slot.Boxes.Length) slot.Boxes[slot.BoxCount] = i;
                    slot.BoxCount++;
                    if (ReferenceEquals(ancestor, _result.Boxes[i].Element) &&
                        _result.Boxes[i].UsedGeometry is { Mode: PositionedMode.Absolute }) slot.Root = i;
                }
        foreach (var slot in _absolutePositions.Values)
        {
            slot.Valid = slot.Root >= 0 && slot.BoxCount == slot.Boxes.Length &&
                (slot.First == null || slot.FirstCount == 1 && slot.LastCount == 1 && slot.End >= slot.Start);
            slot.First = null; slot.Last = null;
            int depth = 0;
            for (var parent = slot.Element.ParentElement; parent != null; parent = parent.ParentElement) depth++;
            slot.Fences = new int[depth];
            for (var parent = slot.Element.ParentElement; parent != null; parent = parent.ParentElement)
                if (_absoluteBoxIndices.TryGetValue(parent, out int index)) slot.Fences[slot.FenceCount++] = index;
        }
        _absoluteAnchors.Clear();
        _absoluteBoxIndices.Clear();
    }

    /// <summary>The caller first consumes an independent inset-only style change. The canonical
    /// solver must preserve size and height definiteness; shrink-to-fit, images and scrollbars
    /// keep their existing full layout path. No CSS position is replaced with an authored transform.</summary>
    public bool TryPrepareAbsolutePositionUpdate(Element element, out AbsolutePositionUpdate update)
    {
        update = default;
        if (!_absolutePositions.TryGetValue(element, out var slot) || !slot.Valid || SampleWidth(element).HasValue) return false;
        var style = _styles.GetComputedStyle(element);
        var plan = PositionedLayoutSolver.Prepare(style, slot.Flow, slot.Absolute, slot.StaticX, slot.StaticY, default);
        var target = PositionedLayoutSolver.Complete(plan, slot.NaturalHeight);
        if (plan.Mode != PositionedMode.Absolute || plan.DefiniteContentHeight.HasValue != slot.HeightDefinite ||
            target.BorderBox.Width != slot.Width || target.BorderBox.Height != slot.Height) return false;
        var before = _result.Boxes[slot.Root].Bounds;
        double dx = target.BorderBox.X - before.X, dy = target.BorderBox.Y - before.Y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return false;
        // Moving overflow beyond an ancestor's padding edge can change scroll extents or
        // clamp an existing scroll offset. Keep the canonical full path in that case.
        // Interior motion cannot alter those extents; no parent metrics are silently stale.
        var current = _result.Boxes[slot.Root];
        for (int i = 0; i < slot.FenceCount; i++)
        {
            var ancestor = _result.Boxes[slot.Fences[i]];
            if (ancestor.UsedGeometry is not { } geometry ||
                ancestor.Element.ScrollState is { } state && (state.Left != 0 || state.Top != 0)) return false;
            var fence = geometry.Context.ClipBounds;
            if (!ContainsBounds(fence, current.Bounds) || !ContainsBounds(fence, current.ScrollOverflow) ||
                !ContainsBounds(fence, target.BorderBox) || !ContainsBounds(fence, Translate(current.ScrollOverflow, dx, dy))) return false;
        }
        update = new(element, _absoluteGeneration, dx, dy);
        return true;
    }

    private void TranslateAbsoluteInputs(LayoutBox box, double dx, double dy, Element? movingRoot = null)
    {
        // An absolute root's own incoming containing block stays put. Nested absolute roots
        // receive the same movement as their containing block, including their static position.
        if (ReferenceEquals(box.Element, movingRoot) || box.UsedGeometry is not { Mode: PositionedMode.Absolute } ||
            !_absolutePositions.TryGetValue(box.Element, out var slot)) return;
        slot.Flow = Translate(slot.Flow, dx, dy); slot.Absolute = Translate(slot.Absolute, dx, dy);
        slot.StaticX += dx; slot.StaticY += dy;
    }

    private static bool ContainsBounds(Rect outer, Rect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    /// <summary>Translate only cached atomic paint spans and used boxes; retain flex/text flow,
    /// authored transforms and ancestor clips. Native paint must separately admit the topology.</summary>
    public bool ApplyAbsolutePositionUpdates(ReadOnlySpan<AbsolutePositionUpdate> updates)
    {
        foreach (ref readonly var update in updates)
            if (update.Generation != _absoluteGeneration || !_absolutePositions.TryGetValue(update.Element, out var slot) || !slot.Valid)
                throw new ArgumentException("Position update does not match the retained layout generation.", nameof(updates));
        bool changed = false;
        _absoluteHitUpdates.Clear();
        try
        {
            foreach (ref readonly var update in updates)
            {
                if (update.X == 0 && update.Y == 0) continue;
                changed = true;
                var slot = _absolutePositions[update.Element];
                for (int i = slot.Start; i >= 0 && i <= slot.End; i++)
                    _result.Runs[i] = TranslatePaintRun(_result.Runs[i], update.X, update.Y);
                foreach (int index in slot.Boxes)
                {
                    var box = _result.Boxes[index];
                    TranslateAbsoluteInputs(box, update.X, update.Y, update.Element);
                    TranslateLayoutBox(box, update.X, update.Y);
                    _absoluteHitUpdates.Add(index);
                }
            }
            _absoluteHitUpdates.Sort();
            int previous = -1;
            foreach (int index in _absoluteHitUpdates)
                if (index != previous) { CompleteHitGeometryAt(index); previous = index; }
            return changed;
        }
        finally { _absoluteHitUpdates.Clear(); }
    }
}
