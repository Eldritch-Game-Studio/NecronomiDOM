using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Layout;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    private readonly DisplayList _textPaint = new();
    private readonly Dictionary<HTMLElement, (Rgba Caret, Rgba Selection)> _textColors = new();
    private readonly Dictionary<HTMLElement, (double X, double Y)> _textScroll = new();
    private (HTMLElement? Owner, string Value, int Start, int End, string? Direction) _caretState;

    private void UpdateTextCaret(bool layoutChanged)
    {
        var owner = Available && HasFocus() ? Document.FocusedElement : null;
        var control = owner as ITextControl;
        var selection = control?.EditorSelection ?? default;
        var state = (owner, control?.Value ?? "", selection.Start, selection.End, selection.Direction);
        bool changed = state != _caretState;
        if (changed) _caretState = state;
        // The caret is drawn whenever it is revealed; there is no blink timer.
        if (layoutChanged) { _textColors.Clear(); _textScroll.Clear(); }
        if (!layoutChanged && !changed) return;
        _textPaint.Runs.Clear(); _textPaint.Boxes.Clear();
        RefreshTextScroll();
        foreach (var run in DisplayList.Runs)
        {
            if (run is TextRun { TextControl: { } element } text) PaintTextControl(text, element);
            else _textPaint.Runs.Add(run);
        }
        _paint.Rebuild(_textPaint, _shaper!, _images!);
        UpdateOpacityGroups();
    }
    private void RefreshTextScroll()
    {
        if (_caretState.Owner is not { } owner || owner is not ITextControl control) return;
        var selection = control.EditorSelection;
        int active = selection.Direction == "backward" ? selection.Start : selection.End;
        foreach (var run in DisplayList.Runs)
        {
            if (run is not TextRun text || !ReferenceEquals(text.TextControl, owner)
                || active < text.TextOffset || active > text.TextOffset + text.Text.Length
                || !text.TextControlLineEnd && active == text.TextOffset + text.Text.Length) continue;
            Rect2 caret = _shaper!.Resolve(text).Caret(text, active - text.TextOffset);
            _textScroll.TryGetValue(owner, out var scroll);
            double x = text.X + caret.Position.X - scroll.X;
            var viewport = text.TextControlViewport;
            if (x < viewport.X) scroll.X -= viewport.X - x;
            else if (x > viewport.X + viewport.Width - 1) scroll.X += x - (viewport.X + viewport.Width - 1);
            if (owner is HTMLTextAreaElement)
            {
                double top = (caret.Size.Y > 0 ? text.Baseline + caret.Position.Y : text.ControlLine.Y) - scroll.Y;
                double bottom = top + (caret.Size.Y > 0 ? caret.Size.Y : text.ControlLine.Height);
                if (top < viewport.Y) scroll.Y -= viewport.Y - top;
                else if (bottom > viewport.Y + viewport.Height) scroll.Y += bottom - (viewport.Y + viewport.Height);
            }
            _textScroll[owner] = scroll; break;
        }
    }
    private void PaintTextControl(TextRun text, HTMLElement element)
    {
        var control = (ITextControl)element;
        bool focused = ReferenceEquals(element, _caretState.Owner);
        var selection = control.EditorSelection;
        int start = selection.Start, end = selection.End;
        int active = selection.Direction == "backward" ? start : end;
        _textScroll.TryGetValue(element, out var scroll);
        if (scroll.X != 0 || scroll.Y != 0) text = text with { X = text.X - scroll.X, Baseline = text.Baseline - scroll.Y };
        var colors = focused ? TextColors(element, text.Color) : default;
        var shaper = _shaper!.Resolve(text);
        var metrics = shaper.Measure(text.Text, text.FontSize, text.FontWeight, text.LetterSpacing, text.FontItalic);
        double top = text.Baseline - metrics.Ascent, height = metrics.Ascent + metrics.Descent;
        int localActive = Math.Clamp(active - text.TextOffset, 0, text.Text.Length);
        Rect2 caret = focused ? shaper.Caret(text, localActive) : default;
        if (focused && start != end)
        {
            int localStart = Math.Clamp(start - text.TextOffset, 0, text.Text.Length);
            int localEnd = Math.Clamp(end - text.TextOffset, 0, text.Text.Length);
            foreach (var span in shaper.Selection(text, localStart, localEnd))
                _textPaint.Runs.Add(TextControlColors.Swatch(new Rect(text.X + span.X, top, span.Y - span.X, height), colors.Selection));
        }
        _textPaint.Runs.Add(text);
        if (focused && !control.ReadOnly && start == end
            && active >= text.TextOffset && (active < text.TextOffset + text.Text.Length
                || text.TextControlLineEnd && active == text.TextOffset + text.Text.Length))
            _textPaint.Runs.Add(TextControlColors.Swatch(new Rect(text.X + caret.Position.X,
                caret.Size.Y > 0 ? text.Baseline + caret.Position.Y : top, 1, caret.Size.Y > 0 ? caret.Size.Y : height), colors.Caret));
    }
    private (Rgba Caret, Rgba Selection) TextColors(HTMLElement element, Rgba foreground)
    {
        if (_textColors.TryGetValue(element, out var cached)) return cached;
        return _textColors[element] = TextControlColors.Resolve(Styles.GetComputedStyle(element), foreground);
    }
    private int? HitTextControl(HTMLElement element, double x, double y)
    {
        foreach (var box in DisplayList.Boxes)
            if (ReferenceEquals(box.Element, element))
            { if (!box.TryMapPoint(x, y, out x, out y)) return null; break; }
        _textScroll.TryGetValue(element, out var scroll); y += scroll.Y;
        TextRun? nearest = null;
        double distance = double.PositiveInfinity;
        foreach (var paint in DisplayList.Runs)
        {
            if (paint is not TextRun { TextControl: { } owner } text || !ReferenceEquals(owner, element)) continue;
            double delta = Math.Abs(y - (text.ControlLine.Y + text.ControlLine.Height / 2));
            if (delta < distance) { distance = delta; nearest = text; }
        }
        if (nearest == null) return null;
        return nearest.TextOffset + _shaper!.Resolve(nearest).HitTest(nearest, x - nearest.X + scroll.X);
    }
    private void ClearTextControls()
    {
        _textPaint.Runs.Clear(); _textPaint.Boxes.Clear(); _textColors.Clear(); _textScroll.Clear();
        _caretState = default;
    }
}
