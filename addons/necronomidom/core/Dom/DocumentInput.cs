using System;
using System.Collections.Generic;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Paint;

namespace NecronomiDom.Dom;

/// <summary>HTML button/link input defaults on the document's actual layout and DOM event dispatcher.</summary>
public sealed partial class DocumentInput : IDisposable
{
    private readonly Document _document;
    private readonly StyleEngine _styles;
    private DisplayList _display = new();
    private readonly Dictionary<HTMLElement, Rect> _bounds = new();
    private readonly List<HTMLElement> _tabOrder = new();
    private readonly Dictionary<HTMLElement, int> _treeOrder = new();
    private readonly HashSet<Element> _hitSurfaces = new();
    private readonly HashSet<Rect> _paintedBounds = new();
    private HTMLElement? _spaceTarget, _enterTarget;
    private Element? _pressed, _hover;
    private bool _suppressMouse, _autofocusProcessed;
    private double _width, _height;
    private BoxRun? _canvasHitSurface;
    private Element? _canvasElement;
    public int PointerActivationCount { get; private set; }
    public DocumentInput(Document document, StyleEngine styles)
    { _document = document; _styles = styles; document.IsRenderedForFocus = IsRendered; document.PointerInput = this; }
    private bool IsRendered(HTMLElement element) => _bounds.TryGetValue(element, out var b) && b.Width > 0 && b.Height > 0;
    public void UpdateGeometry(DisplayList display, double width, double height)
    {
        _display = display; _width = width; _height = height;
        _bounds.Clear(); _treeOrder.Clear(); _tabOrder.Clear(); _hitSurfaces.Clear(); _paintedBounds.Clear();
        _canvasHitSurface = display.Runs.Count > 0 && display.Runs[0] is BoxRun canvas && canvas.Style.Background.A > 0 ? canvas : null;
        _canvasElement = _canvasHitSurface != null && display.Boxes.Count > 0 && display.Boxes[0].PointerEvents ? display.Boxes[0].Element : null;
        if (_canvasElement != null) _hitSurfaces.Add(_canvasElement);
        IndexBounds(display);
        int treeOrder = 0; RecordTreeOrder(_document, ref treeOrder);
        foreach (var run in display.Runs)
            if (run is BoxRun paint && (paint.Style.Background.A > 0 || paint.Style.BorderWidth > 0)) _paintedBounds.Add(paint.Bounds);
            else if (run is BorderBoxRun border && (border.Background.A > 0 || border.Borders.Top.Width > 0
                || border.Borders.Right.Width > 0 || border.Borders.Bottom.Width > 0 || border.Borders.Left.Width > 0))
                _paintedBounds.Add(border.Bounds);
            else if (run is PolygonRun polygon && polygon.Color.A > 0) _paintedBounds.Add(polygon.Bounds);
        foreach (var box in display.Boxes)
        {
            bool surface = box.Element.LocalName is "img" or "input" or "textarea" || ActivationTarget(box.Element) != null || _paintedBounds.Contains(box.Bounds);
            if (!surface) foreach (var child in box.Element.ChildNodes)
                if (child is Text text && !string.IsNullOrWhiteSpace(text.Data)) { surface = true; break; }
            if (surface) _hitSurfaces.Add(box.Element);
        }
        _document.FixupFocus();
        foreach (var element in _bounds.Keys)
            if (element.TabIndex >= 0 && _document.IsFocusable(element)) _tabOrder.Add(element);
        _tabOrder.Sort(CompareTabOrder);
        if (!_autofocusProcessed)
        {
            _autofocusProcessed = true;
            foreach (var box in display.Boxes)
                if (box.Element is HTMLElement { Autofocus: true } element && _document.Focus(element)) break;
        }
        ValidateTransientState();
    }
    public bool MoveSequential(bool backwards)
    {
        _document.SetFocusVisible(true);
        if (_tabOrder.Count == 0) return false;
        var startingPoint = _document.FocusedElement ?? _document.SequentialFocusStartingPoint;
        int index = startingPoint != null ? _tabOrder.IndexOf(startingPoint) : -1;
        if (index < 0 && startingPoint is { } excluded && _treeOrder.TryGetValue(excluded, out int start))
        {
            if (backwards)
            { for (int i = _tabOrder.Count - 1; i >= 0; i--) if (_tabOrder[i].TabIndex == 0 && _treeOrder[_tabOrder[i]] < start) return _document.Focus(_tabOrder[i], true); }
            else
            { foreach (var target in _tabOrder) if (target.TabIndex == 0 && _treeOrder[target] > start) return _document.Focus(target, true); }
        }
        index = index < 0 ? (backwards ? _tabOrder.Count - 1 : 0) : (index + (backwards ? -1 : 1) + _tabOrder.Count) % _tabOrder.Count;
        return _document.Focus(_tabOrder[index], true);
    }
    private static HTMLElement? ActivationTarget(Element? element)
    {
        for (var e = element; e != null; e = e.ParentElement)
            if (e is HTMLElement html && (html.LocalName == "button" || html.LocalName == "a" && html.HasAttribute("href"))) return html;
        return null;
    }
    private static bool Enabled(Element element) => element.IsConnected && !Document.IsInert(element) && !(element.LocalName == "button" && FormValidation.Disabled(element));
    private Element? HitTarget(double x, double y)
    {
        Element? hit = null;
        for (int i = _display.Boxes.Count - 1; i >= 0; i--)
        {
            var box = _display.Boxes[i];
            if (box.PointerEvents && box.Element.IsConnected && box.ContainsPoint(x, y)) { hit = box.Element; break; }
        }
        if (hit == null && _canvasElement?.IsConnected == true && _canvasHitSurface!.Bounds.Contains(x, y)) hit = _canvasElement;
        while (hit != null && Document.IsInert(hit)) hit = hit.ParentElement;
        return hit;
    }
    public bool HasPoint(double x, double y)
    {
        if (HitScrollbar(x, y, out _, out _, out _)) return true;
        for (var target = HitTarget(x, y); target != null; target = target.ParentElement)
            if (_hitSurfaces.Contains(target)) return true;
        return false;
    }
    private static bool Dispatch(Element target, Event e) => target.DispatchUserEvent(e);
    private static MouseEventInit MouseInit(double x, double y, int buttons, Element? related = null, bool bubbles = true) =>
        new() { Bubbles = bubbles, Cancelable = bubbles, Composed = bubbles, ClientX = x, ClientY = y, Buttons = buttons, RelatedTarget = related };
    private static PointerEvent Pointer(string type, double x, double y, int buttons, Element? related = null, bool bubbles = true, int button = -1) =>
        new(type, new PointerEventInit { Bubbles = bubbles, Cancelable = bubbles, Composed = bubbles, ClientX = x, ClientY = y, Buttons = buttons, Button = button, RelatedTarget = related, PointerId = MousePointerId, PointerType = "mouse", IsPrimary = true });
    private void UpdatePointerBoundary(Element? target, double x, double y, int buttons, int button)
    {
        if (ReferenceEquals(target, _hover)) return;
        Element? old = _hover; _hover = target;
        if (old != null)
        {
            old.PseudoState &= ~ElementPseudoState.Hover;
            Dispatch(old, Pointer("pointerout", x, y, buttons, target, button: button));
            for (var e = old; e != null && (target == null || !e.Contains(target)); e = e.ParentElement)
                Dispatch(e, Pointer("pointerleave", x, y, buttons, target, false, button));
        }
        if (target != null)
        {
            target.PseudoState |= ElementPseudoState.Hover;
            Dispatch(target, Pointer("pointerover", x, y, buttons, old, button: button));
            EnterPointer(target, old, x, y, buttons, button);
        }
    }
    private static void EnterPointer(Element target, Element? old, double x, double y, int buttons, int button)
    {
        if (target.ParentElement is { } parent && (old == null || !parent.Contains(old))) EnterPointer(parent, old, x, y, buttons, button);
        Dispatch(target, Pointer("pointerenter", x, y, buttons, old, false, button));
    }
    private void UpdateMouseBoundary(Element? target, double x, double y, int buttons)
    {
        if (ReferenceEquals(target, _mouseHover)) return;
        Element? old = _mouseHover; _mouseHover = target;
        if (old != null)
        {
            Dispatch(old, new MouseEvent("mouseout", MouseInit(x, y, buttons, target)));
            for (var e = old; e != null && (target == null || !e.Contains(target)); e = e.ParentElement)
                Dispatch(e, new MouseEvent("mouseleave", MouseInit(x, y, buttons, target, false)));
        }
        if (target != null)
        {
            Dispatch(target, new MouseEvent("mouseover", MouseInit(x, y, buttons, old)));
            EnterMouse(target, old, x, y, buttons);
        }
    }
    private static void EnterMouse(Element target, Element? old, double x, double y, int buttons)
    {
        if (target.ParentElement is { } parent && (old == null || !parent.Contains(old))) EnterMouse(parent, old, x, y, buttons);
        Dispatch(target, new MouseEvent("mouseenter", MouseInit(x, y, buttons, old, false)));
    }
    public void PointerExit(double x, double y, int buttons = 0)
    {
        if (_disposed || HasPointerCaptureOverride) return;
        ClearScrollbarHover();
        RecordPointer(x, y, buttons);
        UpdatePointerBoundary(null, x, y, buttons, -1);
        UpdateMouseBoundary(null, x, y, buttons);
    }
    public void PointerMove(double x, double y, int buttons = 0)
    {
        if (_disposed) return;
        if (MoveScrollbar(x, y)) return;
        RecordPointer(x, y, buttons);
        int version = _inputVersion;
        Element? eventCapture = ProcessPendingCapture(x, y, buttons, -1, out bool detachedDuringGot);
        if (_disposed || version != _inputVersion) return;
        Element? target = eventCapture ?? _captureTarget as Element ?? HitTarget(x, y);
        UpdatePointerBoundary(target, x, y, buttons, -1);
        UpdateMouseBoundary(target, x, y, buttons);
        if (target != null)
        {
            Dispatch(target, Pointer("pointermove", x, y, buttons));
            if (!_disposed && version == _inputVersion && !_suppressMouse) Dispatch(target, new MouseEvent("mousemove", MouseInit(x, y, buttons)));
            if (!_disposed && version == _inputVersion && !_suppressMouse && _pointerDown) MoveTextSelection(x, y);
        }
        if (detachedDuringGot && !_disposed && version == _inputVersion && target is { IsConnected: false } && _captureTarget == null)
        {
            var physical = HitTarget(x, y);
            UpdatePointerBoundary(physical, x, y, buttons, -1);
            if (!_disposed && version == _inputVersion) UpdateMouseBoundary(physical, x, y, buttons);
        }
    }
    public bool Key(KeyboardEvent input, string? text = null)
    {
        ValidateTransientState();
        var focused = _document.FocusedElement;
        int version = _inputVersion;
        if (input.Type == "keydown") _document.SetFocusVisible(true);
        bool allowed = (focused as EventTarget ?? _document.Body ?? (EventTarget)_document).DispatchUserEvent(input);
        if (!allowed) { if (input.Key == " ") ClearSpace(); if (input.Key == "Enter") ClearEnter(); return false; }
        if (_disposed || version != _inputVersion) return false;
        if (input.Type == "keydown")
        {
            if (input.Key == "Tab") return MoveSequential(input.ShiftKey);
            if (ReferenceEquals(focused, _document.FocusedElement) && EditKey(focused, input, text)) return true;
            if (ScrollKey(focused, input)) return true;
            if (input.Key == "Enter" && ActivationTarget(focused) is { } enter)
            {
                if (enter.LocalName == "button" && Enabled(enter)) { _enterTarget = enter; enter.PseudoState |= ElementPseudoState.Active; }
                return Activate(enter);
            }
            if (input.Key == " " && !input.Repeat && focused is { LocalName: "button" } && Enabled(focused))
            { _spaceTarget = focused; focused.PseudoState |= ElementPseudoState.Active; }
        }
        else if (input.Type == "keyup" && input.Key == " ")
        {
            var space = _spaceTarget; ClearSpace();
            if (space != null && ReferenceEquals(space, _document.FocusedElement)) return Activate(space);
        }
        else if (input.Type == "keyup" && input.Key == "Enter") ClearEnter();
        return true;
    }
    private bool Activate(HTMLElement target)
    {
        if (!Enabled(target)) return false;
        return Dispatch(target, new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true, Composed = true, PointerId = -1, PointerType = "", IsPrimary = false }));
    }
    public bool Submit()
    { _document.SetFocusVisible(true); return ActivationTarget(_document.FocusedElement) is { } target && Activate(target); }
    public void ValidateTransientState()
    {
        _document.FixupFocus();
        if (_pressed != null && !Enabled(_pressed)) ResetPress();
        if (_hover != null && !Enabled(_hover) && !ReferenceEquals(_hover, _captureTarget)) _hover.PseudoState &= ~ElementPseudoState.Hover;
        if (_spaceTarget != null && (!Enabled(_spaceTarget) || !ReferenceEquals(_spaceTarget, _document.FocusedElement))) ClearSpace();
        if (_enterTarget != null && (!Enabled(_enterTarget) || !ReferenceEquals(_enterTarget, _document.FocusedElement))) ClearEnter();
    }
    private void ClearSpace() { if (_spaceTarget != null) _spaceTarget.PseudoState &= ~ElementPseudoState.Active; _spaceTarget = null; }
    private void ClearEnter() { if (_enterTarget != null) _enterTarget.PseudoState &= ~ElementPseudoState.Active; _enterTarget = null; }
    private void ResetPress() { if (_pressed != null) _pressed.PseudoState &= ~ElementPseudoState.Active; _pressed = null; }
    public void ReleaseInput(bool clearFocus = true)
    {
        _inputVersion++;
        PointerCancel();
        ResetPress(); _pointerDownTarget = null; _suppressMouse = false; ClearSpace(); ClearEnter();
        if (_hover != null) _hover.PseudoState &= ~ElementPseudoState.Hover;
        _hover = null; _mouseHover = null;
        if (clearFocus) { _document.Focus(null); _document.SequentialFocusStartingPoint = null; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        ReleaseInput(); _disposed = true; _captureTarget = null; _pendingCaptureTarget = null;
        _pendingScrollEvents.Clear(); _scrollDispatch.Clear(); ScrollChanged = null;
        _document.IsRenderedForFocus = null;
        if (ReferenceEquals(_document.PointerInput, this)) _document.PointerInput = null;
    }

}
