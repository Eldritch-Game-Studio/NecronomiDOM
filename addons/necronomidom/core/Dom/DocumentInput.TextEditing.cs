using System;
using System.Globalization;

namespace NecronomiDom.Dom;

public sealed partial class DocumentInput
{
    private bool EditKey(HTMLElement? target, KeyboardEvent key, string? nativeText)
    {
        if (target is not ITextControl control || TextControlState.For(target) is not { } state) return false;
        if (FormValidation.Disabled(target) || Document.IsInert(target)) return false;
        state.Clamp(control.Value);
        if ((key.CtrlKey || key.MetaKey) && !key.AltKey && key.Key.Equals("a", StringComparison.OrdinalIgnoreCase))
        { state.Set(target, control.Value, 0, control.Value.Length, "forward"); return true; }
        if (key.Key is "ArrowLeft" or "ArrowRight" or "Home" or "End")
        {
            int active = state.Direction == "backward" ? state.Start : state.End;
            int anchor = state.Direction == "backward" ? state.End : state.Start;
            int position = key.Key switch
            {
                "Home" => active == 0 ? 0 : control.Value.LastIndexOf('\n', active - 1, active) + 1,
                "End" => control.Value.IndexOf('\n', active) is int end && end >= 0 ? end : control.Value.Length,
                "ArrowLeft" => !key.ShiftKey && state.Start != state.End ? state.Start : Boundary(control.Value, active, false),
                _ => !key.ShiftKey && state.Start != state.End ? state.End : Boundary(control.Value, active, true)
            };
            if ((key.CtrlKey || key.MetaKey) && (key.Key is "Home" or "End")) position = key.Key == "Home" ? 0 : control.Value.Length;
            state.Set(target, control.Value, key.ShiftKey ? Math.Min(anchor, position) : position,
                key.ShiftKey ? Math.Max(anchor, position) : position, key.ShiftKey ? position < anchor ? "backward" : "forward" : "none");
            return true;
        }
        if (control.ReadOnly) return false;
        if (key.Key == "Enter")
        {
            if (target is HTMLTextAreaElement) ReplaceText(target, control, state, "\n", "insertLineBreak");
            else TextControlState.Commit(target);
            return true;
        }
        if (key.Key is "Backspace" or "Delete")
        {
            int start = state.Start, end = state.End;
            if (start == end)
            {
                if (key.Key == "Backspace") start = Boundary(control.Value, start, false);
                else end = Boundary(control.Value, end, true);
            }
            ReplaceText(target, control, state, "", key.Key == "Backspace" ? "deleteContentBackward" : "deleteContentForward", start, end);
            return true;
        }
        string text = nativeText ?? key.Key;
        if (!key.MetaKey && (!key.CtrlKey && !key.AltKey || nativeText != null && key.CtrlKey && key.AltKey) && Printable(text))
        { ReplaceText(target, control, state, text, "insertText"); return true; }
        return false;
    }
    private static bool Printable(string value)
    {
        if (value.Length == 0) return false;
        int elements = 0;
        foreach (var rune in value.EnumerateRunes()) { if (RuneControl(rune.Value)) return false; elements++; }
        return elements == 1;
    }
    private static bool RuneControl(int value) => value < 32 || value is >= 127 and <= 159;
    private static int Boundary(string value, int position, bool forward)
    {
        if (position == (forward ? value.Length : 0)) return position;
        // StringInfo follows Unicode grapheme boundaries; never leave a surrogate or combining mark behind.
        int previous = 0;
        for (int boundary = 0; boundary < value.Length;)
        {
            if (forward && boundary > position) return boundary;
            if (!forward && boundary >= position) return previous;
            previous = boundary;
            boundary += StringInfo.GetNextTextElementLength(value.AsSpan(boundary));
        }
        return forward ? value.Length : previous;
    }
    private void ReplaceText(HTMLElement target, ITextControl control, TextControlState state, string text, string inputType,
        int? from = null, int? to = null)
    {
        string old = control.Value;
        int start = from ?? state.Start, end = to ?? state.End;
        int version = _inputVersion;
        int selectionStart = state.Start, selectionEnd = state.End;
        string? data = inputType.StartsWith("delete", StringComparison.Ordinal) || inputType == "insertLineBreak" ? null : text;
        if (!Dispatch(target, new InputEvent("beforeinput", new InputEventInit
            { Bubbles = true, Cancelable = true, Composed = true, Data = data, InputType = inputType }))) return;
        if (_disposed || version != _inputVersion || !ReferenceEquals(target, _document.FocusedElement)
            || control.ReadOnly || FormValidation.Disabled(target) || Document.IsInert(target)) return;
        // A beforeinput listener may replace value/selection. Use its current range rather than stale offsets.
        if (control.Value != old || state.Start != selectionStart || state.End != selectionEnd)
        { old = control.Value; state.Clamp(old); start = state.Start; end = state.End; }
        text = ConstrainInsertion(target, old, start, end, text);
        if (start == end && text.Length == 0) return;
        if (data != null) data = text;
        string next = old[..start] + text + old[end..];
        string? pending = state.PendingValue;
        control.SetUserValue(next);
        if (control.Value == old) return;
        state.PendingValue = pending ?? old;
        int caret = Math.Min(start + text.Length, control.Value.Length);
        state.Set(target, control.Value, caret, caret, "none");
        Dispatch(target, new InputEvent("input", new InputEventInit { Bubbles = true, Composed = true, Data = data, InputType = inputType }));
    }
    private static string ConstrainInsertion(HTMLElement target, string value, int start, int end, string text)
    {
        int available = Math.Max(0, TextControlState.MaxLength(target) - (value.Length - (end - start)));
        if (text.Length <= available) return text;
        if (available > 0 && char.IsHighSurrogate(text[available - 1])) available--;
        return text[..available];
    }
}
