using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>
/// One store per DocumentPaint. Retains ordered polygon/context native nodes and GPU buffers across dirty rebuilds;
/// detached nodes remain owned here until their slot disappears or the document painter is disposed.
/// </summary>
public sealed class PolygonPaintStore : IDisposable
{
    private readonly record struct Slot(Type Kind, CanvasItem Item);
    private readonly List<Slot> _slots = new();
    private int _cursor;

    /// <summary>Must precede normal-item cleanup, including DocumentHost's separate Clear calls.</summary>
    public void DetachBeforeClear()
    {
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            CanvasItem item = _slots[i].Item;
            item.GetParent()?.RemoveChild(item);
        }
    }

    public void BeginRebuild() => _cursor = 0;
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
        Type kind = run.GetType();
        if (_cursor < _slots.Count && _slots[_cursor].Kind == kind)
        {
            item = _slots[_cursor].Item;
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
                _ => throw new NotSupportedException("Unknown native polygon store run: " + kind.FullName)
            };
            var slot = new Slot(kind, item);
            if (_cursor == _slots.Count) _slots.Add(slot);
            else { _slots[_cursor].Item.Free(); _slots[_cursor] = slot; }
        }
        if (item is PolygonComposite composite) composite.BeginRebuild();
        _cursor++;
        return item;
    }

    public void EndRebuild()
    {
        for (int i = _slots.Count - 1; i >= _cursor; i--) _slots[i].Item.Free();
        if (_cursor < _slots.Count) _slots.RemoveRange(_cursor, _slots.Count - _cursor);

    }

    public void Dispose()
    {
        DetachBeforeClear();
        for (int i = _slots.Count - 1; i >= 0; i--) _slots[i].Item.Free();
        _slots.Clear(); _cursor = 0;
    }
}
