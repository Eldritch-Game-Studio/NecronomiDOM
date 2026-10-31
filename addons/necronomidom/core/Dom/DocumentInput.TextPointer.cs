using System;

namespace NecronomiDom.Dom;

public sealed partial class DocumentInput
{
    /// <summary>Native shaped-text hit test; pure clients may provide their actual text geometry.</summary>
    public Func<HTMLElement, double, double, int?>? TextHitTest { get; set; }
    private HTMLElement? _textDrag;
    private int _textAnchor;
    private void BeginTextSelection(HTMLElement? target, double x, double y)
    {
        _textDrag = null;
        if (target is not ITextControl control || TextControlState.For(target) is not { } state
            || !ReferenceEquals(target, _document.FocusedElement)) return;
        int position = TextHitTest?.Invoke(target, x, y) ?? control.Value.Length;
        state.Set(target, control.Value, position, position, "none");
        _textDrag = target; _textAnchor = position;
    }
    private void MoveTextSelection(double x, double y)
    {
        if (_textDrag is not { } target || !ReferenceEquals(target, _document.FocusedElement) || !target.IsConnected
            || Document.IsInert(target) || FormValidation.Disabled(target)) { _textDrag = null; return; }
        if (target is not ITextControl control || TextControlState.For(target) is not { } state
            || TextHitTest?.Invoke(target, x, y) is not int position) return;
        state.Set(target, control.Value, Math.Min(_textAnchor, position), Math.Max(_textAnchor, position), position < _textAnchor ? "backward" : "forward");
    }
}
