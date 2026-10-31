using System;
using System.Buffers;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private sealed class PaintPhaseFrame
    {
        internal int RunStart, HitStart;
        internal readonly List<PositionedPaintFragment> Positioned = new();
    }
    private sealed class OverflowPaintLink(PaintRun begin, PaintRun end, double x, double y, OverflowPaintLink? parent)
    {
        internal readonly PaintRun Begin = begin, End = end;
        internal readonly double X = x, Y = y;
        internal readonly OverflowPaintLink? Parent = parent;
    }
    private struct PositionedPaintFragment
    {
        internal Element? ContainingElement;
        internal PositionedMode Mode;
        internal int Order, RunCount, HitCount;
        internal PaintRun[] Runs;
        internal int[] Hits;
        internal double X, Y;
        internal OverflowPaintLink? Overflow;
    }
    private readonly List<PaintPhaseFrame> _paintFrames = new();
    private readonly List<int> _paintHitOrder = new();
    private readonly Dictionary<Element, int> _paintTreeOrder = new();
    private readonly Comparison<PositionedPaintFragment> _comparePaintFragments = static (a, b) => a.Order.CompareTo(b.Order);
    private int _paintDepth;

    private void BeginPaintPhases(Node root)
    {
        _paintDepth = 0; _paintHitOrder.Clear(); _paintTreeOrder.Clear();
        RecordPaintTreeOrder(root);
    }
    private void RecordPaintTreeOrder(Node node)
    {
        if (node is Element element) _paintTreeOrder.Add(element, _paintTreeOrder.Count);
        for (var child = node.FirstChild; child != null; child = child.NextSibling) RecordPaintTreeOrder(child);
    }
    private PaintPhaseFrame EnterPaintPhase(int boxIndex)
    {
        if (_paintDepth == _paintFrames.Count) _paintFrames.Add(new PaintPhaseFrame());
        var frame = _paintFrames[_paintDepth++];
        frame.RunStart = _result.Runs.Count; frame.HitStart = _paintHitOrder.Count;
        _paintHitOrder.Add(boxIndex);
        return frame;
    }
    private void FlushPositionedPaint(PaintPhaseFrame frame)
    {
        frame.Positioned.Sort(_comparePaintFragments);
        try
        {
            foreach (var fragment in frame.Positioned)
            {
                if (fragment.RunCount > 0)
                {
                    for (var clip = fragment.Overflow; clip != null; clip = clip.Parent)
                        _result.Runs.Add(TranslatePaintRun(clip.Begin, fragment.X - clip.X, fragment.Y - clip.Y));
                    for (int i = 0; i < fragment.RunCount; i++)
                        _result.Runs.Add(TranslatePaintRun(fragment.Runs[i], fragment.X, fragment.Y));
                    CloseOverflowPaint(fragment.Overflow);
                }
                for (int i = 0; i < fragment.HitCount; i++) _paintHitOrder.Add(fragment.Hits[i]);
            }
        }
        finally
        {
            foreach (var fragment in frame.Positioned) ReturnPaintFragment(fragment);
            frame.Positioned.Clear();
        }
    }
    private void CloseOverflowPaint(OverflowPaintLink? clip)
    {
        if (clip == null) return;
        CloseOverflowPaint(clip.Parent); _result.Runs.Add(clip.End);
    }
    private static void ReturnPaintFragment(PositionedPaintFragment fragment)
    {
        if (fragment.RunCount > 0)
        { Array.Clear(fragment.Runs, 0, fragment.RunCount); ArrayPool<PaintRun>.Shared.Return(fragment.Runs); }
        if (fragment.HitCount > 0) ArrayPool<int>.Shared.Return(fragment.Hits);
    }
    private void ReleasePaintPhases()
    {
        foreach (var frame in _paintFrames)
        { foreach (var fragment in frame.Positioned) ReturnPaintFragment(fragment); frame.Positioned.Clear(); }
        _paintDepth = 0;
    }
    private void CompletePaintHitOrder()
    {
        if (_paintHitOrder.Count != _result.Boxes.Count) throw new InvalidOperationException("Paint phase lost a layout box.");
        var boxes = ArrayPool<LayoutBox>.Shared.Rent(_result.Boxes.Count);
        try
        {
            _result.Boxes.CopyTo(boxes);
            for (int i = 0; i < _paintHitOrder.Count; i++) _result.Boxes[i] = boxes[_paintHitOrder[i]];
        }
        finally { Array.Clear(boxes, 0, _result.Boxes.Count); ArrayPool<LayoutBox>.Shared.Return(boxes); }
    }
}
