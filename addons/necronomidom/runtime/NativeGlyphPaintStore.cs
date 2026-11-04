#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Reuses owned glyph targets through structural repaint; slots are rebound, not matched by appearance.</summary>
public sealed class NativeGlyphPaintStore : IDisposable
{
    private sealed class Slot(TextEffectsRun run, NativeGlyphEffectsPaint paint)
    {
        internal TextEffectsRun Run = run;
        internal readonly NativeGlyphEffectsPaint Paint = paint;
    }
    private readonly List<Slot> _slots = new();
    private int _cursor;
    public void BeginRebuild() => _cursor = 0;
    public NativeGlyphEffectsPaint Create(TextEffectsRun run, INativeFontFamilies fonts, out int index)
    {
        index = _cursor++;
        if (index == _slots.Count)
        {
            var shaped = fonts.Resolve(run.Text).GetShapedRun(run.Text);
            _slots.Add(new(run, new NativeGlyphEffectsPaint(run, shaped)));
        }
        else Update(index, run, fonts);
        return _slots[index].Paint;
    }
    internal bool Owns(int index, CanvasItem? item) => (uint)index < (uint)_slots.Count
        && ReferenceEquals(_slots[index].Paint, item);
    internal void Update(int index, TextEffectsRun run, INativeFontFamilies fonts)
    {
        var slot = _slots[index];
        if (Equals(slot.Run, run)) return;
        slot.Paint.Update(run, fonts.Resolve(run.Text).GetShapedRun(run.Text));
        slot.Run = run;
    }
    public void DetachBeforeClear()
    {
        foreach (var slot in _slots) slot.Paint.GetParent()?.RemoveChild(slot.Paint);
    }
    public void EndRebuild()
    {
        for (int i = _slots.Count - 1; i >= _cursor; i--) _slots[i].Paint.Free();
        if (_cursor < _slots.Count) _slots.RemoveRange(_cursor, _slots.Count - _cursor);
    }
    public void Dispose()
    {
        DetachBeforeClear();
        foreach (var slot in _slots) slot.Paint.Free();
        _slots.Clear(); _cursor = 0;
    }
}
