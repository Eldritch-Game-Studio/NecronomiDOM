using System.Collections.Generic;

namespace NecronomiDom.Dom;

public sealed partial class HTMLInputElement : ITextControl
{
    private TextControlState? _editing;
    internal TextControlState Editing => _editing ??= new();
    (int Start, int End, string Direction) ITextControl.EditorSelection
    { get { Editing.Clamp(Value); return (Editing.Start, Editing.End, Editing.Direction); } }
    public bool IsTextEditable => Type is "text" or "search" or "tel" or "url" or "email" or "password" or "number";
    private bool SelectionApplies => Type is "text" or "search" or "tel" or "url" or "password";
    private void RequireSelection() { if (!SelectionApplies) throw DomException.InvalidState("Selection does not apply to input type " + Type); }
    public int? SelectionStart
    {
        get { if (!SelectionApplies) return null; Editing.Clamp(Value); return Editing.Start; }
        set { RequireSelection(); int start = TextControlState.Offset(value ?? 0, Value.Length); SetSelectionRange(start, System.Math.Max(start, Editing.End), Editing.Direction); }
    }
    public int? SelectionEnd
    {
        get { if (!SelectionApplies) return null; Editing.Clamp(Value); return Editing.End; }
        set { RequireSelection(); SetSelectionRange(Editing.Start, value ?? 0, Editing.Direction); }
    }
    public string? SelectionDirection
    {
        get => SelectionApplies ? Editing.Direction : null;
        set { RequireSelection(); SetSelectionRange(Editing.Start, Editing.End, value ?? "none"); }
    }
    public void SetSelectionRange(int start, int end, string direction = "none")
    { RequireSelection(); Editing.Set(this, Value, start, end, direction); }
    public void Select() { if (IsTextEditable) Editing.Set(this, Value, 0, Value.Length, "none"); }
}

public sealed partial class HTMLTextAreaElement : ITextControl
{
    internal readonly TextControlState Editing = new();
    public int SelectionStart
    {
        get { Editing.Clamp(Value); return Editing.Start; }
        set { int start = TextControlState.Offset(value, Value.Length); SetSelectionRange(start, System.Math.Max(start, Editing.End), Editing.Direction); }
    }
    public int SelectionEnd
    {
        get { Editing.Clamp(Value); return Editing.End; }
        set => SetSelectionRange(Editing.Start, value, Editing.Direction);
    }
    public string SelectionDirection
    {
        get => Editing.Direction;
        set => SetSelectionRange(Editing.Start, Editing.End, value);
    }
    (int Start, int End, string Direction) ITextControl.EditorSelection
    { get { Editing.Clamp(Value); return (Editing.Start, Editing.End, Editing.Direction); } }
    public void SetSelectionRange(int start, int end, string direction = "none") => Editing.Set(this, Value, start, end, direction);
    public void Select() => SetSelectionRange(0, Value.Length);
}

public partial class Document
{
    private readonly Queue<HTMLElement> _textSelectionEvents = new();
    internal void QueueTextSelectionEvent(HTMLElement owner) => _textSelectionEvents.Enqueue(owner);
    /// <summary>Host task checkpoint; bounded to events already queued when this turn starts.</summary>
    public void DispatchTextSelectionEvents()
    {
        int count = _textSelectionEvents.Count;
        while (count-- > 0) _textSelectionEvents.Dequeue().DispatchUserEvent(new Event("select", new EventInit { Bubbles = true }));
    }
}
