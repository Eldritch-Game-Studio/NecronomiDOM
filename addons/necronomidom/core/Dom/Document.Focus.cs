using System;
using System.Globalization;

namespace NecronomiDom.Dom;

public partial class Document
{
    public HTMLElement? FocusedElement { get; private set; }
    internal HTMLElement? SequentialFocusStartingPoint { get; set; }
    public Element? ActiveElement => FocusedElement ?? (Element?)Body ?? DocumentElement;
    /// <summary>Engine-provided rendered focusability; null for layout-independent DOM clients.</summary>
    public Func<HTMLElement, bool>? IsRenderedForFocus { get; set; }
    public bool FocusVisible { get; private set; } = true;
    private int _focusVersion;
    private bool _keyboardModality = true;

    public bool IsFocusable(HTMLElement element) =>
        IsFocusCandidate(element) && (IsRenderedForFocus?.Invoke(element) ?? true);

    /// <summary>HTML focus eligibility before the engine's rendered geometry gate.</summary>
    public bool IsFocusCandidate(HTMLElement element)
    {
        if (!ReferenceEquals(element.NodeDocument, this) || !element.IsConnected || IsInert(element)) return false;
        if (element.LocalName is "button" or "input" or "select" or "textarea" && FormValidation.Disabled(element)) return false;
        if (element is HTMLInputElement { Type: "hidden" }) return false;
        for (Element? ancestor = element; ancestor != null; ancestor = ancestor.ParentElement)
            if (ancestor.HasAttribute("hidden")) return false;
        bool explicitIndex = int.TryParse(element.GetAttribute("tabindex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        return explicitIndex || element.LocalName is "button" or "input" or "select" or "textarea" or "summary" || element.LocalName == "a" && element.HasAttribute("href");
    }
    public static bool IsInert(Element element)
    {
        for (Node? node = element; node != null; node = node.ParentNode ?? (node is ShadowRoot root ? root.Host : null))
            if (node is Element e && e.HasAttribute("inert")) return true;
        return false;
    }
    public void SetFocusVisible(bool visible)
    { _keyboardModality = visible; UpdateFocusRing(visible); }
    internal bool FocusFromPointer(HTMLElement? element)
    {
        // Same-target focusing is idempotent, including modality; explicit ring options are separate.
        if (ReferenceEquals(element, FocusedElement)) return true;
        _keyboardModality = false;
        return Focus(element, false);
    }
    private void UpdateFocusRing(bool visible)
    {
        FocusVisible = visible;
        if (FocusedElement is { } current)
            current.PseudoState = visible ? current.PseudoState | ElementPseudoState.FocusVisible : current.PseudoState & ~ElementPseudoState.FocusVisible;
    }
    public bool Focus(HTMLElement? element, bool? focusVisible = null, bool preventScroll = false)
    {
        if (element != null && !IsFocusable(element)) return false;
        if (ReferenceEquals(element, FocusedElement)) return true;
        int version = ++_focusVersion;
        var old = FocusedElement;
        bool visible = focusVisible ?? (old != null ? (old.PseudoState & ElementPseudoState.FocusVisible) != 0 : _keyboardModality);
        FocusedElement = null;
        if (old != null)
        {
            TextControlState.Commit(old);
            if (version != _focusVersion) return ReferenceEquals(FocusedElement, element);
            old.PseudoState &= ~(ElementPseudoState.Focus | ElementPseudoState.FocusVisible);
            old.DispatchUserEvent(new FocusEvent("blur", new FocusEventInit { Composed = true, RelatedTarget = element }));
            if (version != _focusVersion) return ReferenceEquals(FocusedElement, element);
            old.DispatchUserEvent(new FocusEvent("focusout", new FocusEventInit { Bubbles = true, Composed = true, RelatedTarget = element }));
            if (version != _focusVersion) return ReferenceEquals(FocusedElement, element);
        }
        if (element == null || !IsFocusable(element)) return element == null;
        FocusedElement = element;
        SequentialFocusStartingPoint = element;
        element.PseudoState |= ElementPseudoState.Focus;
        UpdateFocusRing(visible);
        if (!preventScroll) PointerInput?.ScrollIntoView(element, focus: true);
        element.DispatchUserEvent(new FocusEvent("focus", new FocusEventInit { Composed = true, RelatedTarget = old }));
        if (version != _focusVersion || !ReferenceEquals(FocusedElement, element)) return ReferenceEquals(FocusedElement, element);
        element.DispatchUserEvent(new FocusEvent("focusin", new FocusEventInit { Bubbles = true, Composed = true, RelatedTarget = old }));
        return ReferenceEquals(FocusedElement, element);
    }
    public void FixupFocus()
    {
        if (FocusedElement is { } current && !IsFocusable(current)) Focus(null);
    }
    internal void FocusedSubtreeRemoved(Node subtree)
    {
        if (SequentialFocusStartingPoint is { } start && subtree.IsShadowIncludingInclusiveAncestorOf(start))
            SequentialFocusStartingPoint = null;
        if (FocusedElement is not { } current || !subtree.IsShadowIncludingInclusiveAncestorOf(current)) return;
        // HTML unfocusing on removal does not dispatch blur/focusout on a detached element.
        ++_focusVersion; FocusedElement = null;
        current.PseudoState &= ~(ElementPseudoState.Focus | ElementPseudoState.FocusVisible);
    }
}
public partial class HTMLElement
{
    public bool Autofocus { get => HasAttribute("autofocus"); set => ToggleAttribute("autofocus", value); }
    public void Focus(FocusOptions? options = null) => NodeDocument.Focus(this, options?.FocusVisible, options?.PreventScroll ?? false);
    public void Blur() { if (ReferenceEquals(NodeDocument.FocusedElement, this)) NodeDocument.Focus(null); }
    private bool _clickInProgress;
    public void Click()
    {
        if (_clickInProgress || LocalName == "button" && FormValidation.Disabled(this)) return;
        _clickInProgress = true;
        try { DispatchEvent(new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true, Composed = true, PointerId = -1, PointerType = "" })); }
        finally { _clickInProgress = false; }
    }
}
public sealed class FocusOptions
{
    public bool PreventScroll { get; init; }
    public bool? FocusVisible { get; init; }
}
