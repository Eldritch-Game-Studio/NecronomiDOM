#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>
/// One store per DocumentPaint. Retains polygon/context nodes by concrete paint kind across dirty rebuilds;
/// a different-kind insertion cannot evict unrelated targets. Unused slots still retire at the end of each rebuild.
/// </summary>
public sealed class PolygonPaintStore : IDisposable
{
    private enum SlotKind { Polygon, PolygonClip, RectClip, RoundedRectClip, Opacity, Count }
    private readonly List<CanvasItem>?[] _slots = new List<CanvasItem>?[(int)SlotKind.Count];
    private readonly int[] _cursors = new int[(int)SlotKind.Count];

    /// <summary>Must precede normal-item cleanup, including DocumentHost's separate Clear calls.</summary>
    public void DetachBeforeClear()
    {
        foreach (var slots in _slots)
        {
            if (slots == null) continue;
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                CanvasItem item = slots[i];
                item.GetParent()?.RemoveChild(item);
            }
        }
    }

    public void BeginRebuild() => Array.Clear(_cursors);
    public CanvasItem Create(PolygonRun run) => Acquire(run);
    public CanvasItem CreateContext(PaintRun begin)
    {
        if (begin is not (BeginRectClipRun or BeginRoundedRectClipRun or BeginOpacityRun or BeginPolygonClipRun))
            throw new NotSupportedException("Unknown native paint context: " + begin.GetType().FullName);
        return Acquire(begin);
    }
    public CanvasItem Content(CanvasItem context) => context is PolygonComposite composite ? composite.Content :
        throw new NotSupportedException("Unknown isolated native context: " + context.GetType().FullName);

    private CanvasItem Acquire(PaintRun run)
    {
        CanvasItem item;
        int kind = (int)(run switch
        {
            PolygonRun => SlotKind.Polygon,
            BeginPolygonClipRun => SlotKind.PolygonClip,
            BeginRectClipRun => SlotKind.RectClip,
            BeginRoundedRectClipRun => SlotKind.RoundedRectClip,
            BeginOpacityRun => SlotKind.Opacity,
            _ => throw new NotSupportedException("Unknown native polygon store run: " + run.GetType().FullName)
        });
        var slots = _slots[kind] ??= new List<CanvasItem>();
        ref int cursor = ref _cursors[kind];
        if (cursor < slots.Count)
        {
            item = slots[cursor];
            switch (run, item)
            {
                case (PolygonRun polygon, PolygonPaint paint): paint.Update(polygon); break;
                case (BeginPolygonClipRun clip, PolygonClipGroup group): group.Update(clip); break;
                case (BeginRectClipRun clip, RoundedRectClipGroup group): group.Update(clip.Bounds, 0); break;
                case (BeginRoundedRectClipRun clip, RoundedRectClipGroup group): group.Update(clip.Bounds, clip.Radius); break;
                case (BeginOpacityRun opacity, PolygonOpacityGroup group): group.Update(opacity.Opacity); break;
                default: throw new InvalidOperationException("Native polygon store slot does not match its declared paint kind.");
            }
        }
        else
        {
            item = run switch
            {
                PolygonRun polygon => new PolygonPaint(polygon),
                BeginPolygonClipRun clip => new PolygonClipGroup(clip),
                BeginRectClipRun clip => new RoundedRectClipGroup(clip.Bounds, 0),
                BeginRoundedRectClipRun clip => new RoundedRectClipGroup(clip.Bounds, clip.Radius),
                BeginOpacityRun opacity => new PolygonOpacityGroup(opacity.Opacity),
                _ => throw new InvalidOperationException("Native polygon store kind has no constructor.")
            };
            slots.Add(item);
        }
        if (item is PolygonComposite composite) composite.BeginRebuild();
        cursor++;
        return item;
    }

    public void EndRebuild()
    {
        for (int kind = 0; kind < _slots.Length; kind++)
        {
            var slots = _slots[kind];
            if (slots == null) continue;
            int cursor = _cursors[kind];
            for (int i = slots.Count - 1; i >= cursor; i--) slots[i].Free();
            if (cursor < slots.Count) slots.RemoveRange(cursor, slots.Count - cursor);
        }
    }

    public void Dispose()
    {
        DetachBeforeClear();
        foreach (var slots in _slots)
        {
            if (slots == null) continue;
            for (int i = slots.Count - 1; i >= 0; i--) slots[i].Free();
            slots.Clear();
        }
        Array.Clear(_cursors);
    }
}
