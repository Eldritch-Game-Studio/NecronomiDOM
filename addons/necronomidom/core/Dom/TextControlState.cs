using System;
using System.Globalization;

namespace NecronomiDom.Dom;

/// <summary>Editor adapter; UTF-16 caret state remains available even when the HTML selection IDL does not apply.</summary>
public interface ITextControl
{
    string Value { get; set; }
    bool ReadOnly { get; }
    (int Start, int End, string Direction) EditorSelection { get; }
    void SetUserValue(string value);
}

internal sealed class TextControlState
{
    internal int Start, End;
    internal string Direction = "none";
    internal string? PendingValue;
    internal void Clamp(string value) { Start = Math.Min(Start, value.Length); End = Math.Min(End, value.Length); }
    internal static int Offset(int offset, int length) => (int)Math.Min(unchecked((uint)offset), (uint)length);
    internal void ValueChanged(string value)
    { Start = End = value.Length; Direction = "none"; }
    internal void Set(HTMLElement owner, string value, int start, int end, string direction)
    {
        end = Offset(end, value.Length); start = Math.Min(Offset(start, value.Length), end);
        direction = direction is "forward" or "backward" ? direction : "none";
        if (Start == start && End == end && Direction == direction) return;
        Start = start; End = end; Direction = direction;
        owner.NodeDocument.QueueTextSelectionEvent(owner);
    }
    internal static TextControlState? For(HTMLElement? owner) => owner switch
    { HTMLInputElement input when input.IsTextEditable => input.Editing, HTMLTextAreaElement area => area.Editing, _ => null };
    internal static int MaxLength(HTMLElement owner) => owner is HTMLInputElement { Type: "number" } ? int.MaxValue
        : int.TryParse(owner.GetAttribute("maxlength"), NumberStyles.None, CultureInfo.InvariantCulture, out int length) ? length : int.MaxValue;
    internal static void Commit(HTMLElement? owner)
    {
        if (owner is not ITextControl control || For(owner) is not { } state || state.PendingValue == null) return;
        string previous = state.PendingValue; state.PendingValue = null;
        if (previous != control.Value) owner.DispatchUserEvent(new Event("change", new EventInit { Bubbles = true }));
    }
}
