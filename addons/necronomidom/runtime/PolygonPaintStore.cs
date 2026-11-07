#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>
/// One store per DocumentPaint. Retains polygon/context nodes by concrete paint kind across dirty rebuilds;
/// a different-kind insertion cannot evict unrelated targets. Unused polygon slots retire at the end of each rebuild;
/// up to <see cref="SpareIsolationTargets"/> unused isolation targets per kind stay detached for the next rebuild.
/// </summary>
public sealed class PolygonPaintStore : IDisposable
{
    private enum SlotKind { Polygon, SinglePolygon, PolygonClip, RectClip, RoundedRectClip, Opacity, Count }
    private readonly List<CanvasItem>?[] _slots = new List<CanvasItem>?[(int)SlotKind.Count];
    private readonly int[] _cursors = new int[(int)SlotKind.Count];

    /// <summary>Detached isolation targets (clip/opacity composites) retained per kind beyond the last rebuild's use.
    /// A document whose context count oscillates, such as an opacity transition entering and leaving 1, would otherwise
    /// free and recreate a SubViewport, World2D and material on every structural rebuild. Detached spares do not draw;
    /// they keep their render target until reused, evicted by a smaller later rebuild, or disposed with the store.</summary>
    public const int SpareIsolationTargets = 8;

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
    public CanvasItem Create(PolygonRun run) => Acquire(run, false);
    internal CanvasItem CreateSingle(PolygonRun run) => Acquire(run, true);
    public CanvasItem CreateContext(PaintRun begin)
    {
        if (begin is not (BeginRectClipRun or BeginRoundedRectClipRun or BeginOpacityRun or BeginPolygonClipRun))
            throw new NotSupportedException("Unknown native paint context: " + begin.GetType().FullName);
        return Acquire(begin, false);
    }
    public CanvasItem Content(CanvasItem context) => context is PolygonComposite composite ? composite.Content :
        throw new NotSupportedException("Unknown isolated native context: " + context.GetType().FullName);

    private CanvasItem Acquire(PaintRun run, bool singleFill)
    {
        CanvasItem item;
        int kind = singleFill ? (int)SlotKind.SinglePolygon : (int)(run switch
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
                case (BeginOpacityRun opacity, PolygonOpacityGroup group): group.Update(opacity.Opacity, false); break;
                default: throw new InvalidOperationException("Native polygon store slot does not match its declared paint kind.");
            }
        }
        else
        {
            item = run switch
            {
                PolygonRun polygon => new PolygonPaint(polygon, singleFill),
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
            int keep = _cursors[kind];
            if (kind >= (int)SlotKind.PolygonClip) keep = Math.Min(slots.Count, keep + SpareIsolationTargets);
            for (int i = slots.Count - 1; i >= keep; i--) slots[i].Free();
            if (keep < slots.Count) slots.RemoveRange(keep, slots.Count - keep);
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
