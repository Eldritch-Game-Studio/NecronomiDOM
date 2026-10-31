namespace NecronomiDom.Dom;

public sealed partial class DocumentInput
{
    private const int MousePointerId = 1;
    private Element? _pendingCaptureTarget, _mouseHover, _pointerDownTarget;
    private EventTarget? _captureTarget;
    private bool _pointerKnown, _pointerDown, _disposed, _processingCapture, _cancellingPointer;
    private int _pointerButtons, _inputVersion;
    private double _pointerX, _pointerY;
    /// <summary>Native routing must remain active until pending loss has actually been processed.</summary>
    public bool HasPointerCaptureOverride => !_disposed && (_captureTarget != null || _pendingCaptureTarget != null);
    private static bool PointerTargetEnabled(Element target, bool captured) => (captured || target.IsConnected && !Document.IsInert(target))
        && !(target.LocalName == "button" && FormValidation.Disabled(target));
    public void PointerButton(double x, double y, bool down)
    {
        if (!_disposed && ScrollbarButton(x, y, down)) return;
        if (_disposed || !down && !_pointerDown && !HasPointerCaptureOverride) return;
        ValidateTransientState();
        RecordPointer(x, y, down ? 1 : 0);
        _pointerDown = down;
        int version = _inputVersion;
        Element? eventCapture = ProcessPendingCapture(x, y, down ? 1 : 0, 0, out _);
        if (_disposed || version != _inputVersion) return;
        var target = eventCapture ?? _captureTarget as Element ?? HitTarget(x, y);
        bool captured = eventCapture != null || _captureTarget is Element;
        // DOM removal/capture changes can retarget a stationary pointer before its next button event.
        UpdatePointerBoundary(target, x, y, down ? 1 : 0, 0);
        UpdateMouseBoundary(target, x, y, down ? 1 : 0);
        if (_disposed || version != _inputVersion) return;
        if (down)
        {
            ResetPress();
            _pressed = target; _pointerDownTarget = target; _suppressMouse = false;
            _document.SequentialFocusStartingPoint = target as HTMLElement;
            if (target == null) { _document.FocusFromPointer(null); return; }
            target.PseudoState |= ElementPseudoState.Active;
            _suppressMouse = !Dispatch(target, Pointer("pointerdown", x, y, 1, button: 0));
            if (_disposed || version != _inputVersion) return;
            if (!_suppressMouse && (ActivationTarget(target) is not { } button || PointerTargetEnabled(button, captured)) && Dispatch(target, new MouseEvent("mousedown", MouseInit(x, y, 1))))
            {
                if (_disposed || version != _inputVersion) return;
                HTMLElement? focus = target as HTMLElement;
                while (focus != null && !_document.IsFocusable(focus)) focus = focus.ParentElement as HTMLElement;
                _document.FocusFromPointer(focus);
                if (!_disposed && version == _inputVersion) BeginTextSelection(focus, x, y);
            }
        }
        else
        {
            if (!_suppressMouse) MoveTextSelection(x, y);
            _textDrag = null;
            var pressed = _pointerDownTarget;
            if (target != null)
            {
                Dispatch(target, Pointer("pointerup", x, y, 0, button: 0));
                if (_disposed || version != _inputVersion) return;
                if (!_suppressMouse && (ActivationTarget(target) is not { } button || PointerTargetEnabled(button, captured))) Dispatch(target, new MouseEvent("mouseup", MouseInit(x, y, 0)));
            }
            if (_disposed || version != _inputVersion) return;
            ResetPress(); _pointerDownTarget = null; _suppressMouse = false;
            _pendingCaptureTarget = null;
            ProcessPendingCapture(x, y, 0, 0, out _);
            if (_disposed || version != _inputVersion) return;
            Element? clickTarget = captured ? target : pressed;
            if (!captured)
                while (clickTarget != null && (target == null || !clickTarget.Contains(target))) clickTarget = clickTarget.ParentElement;
            if ((captured || pressed != null) && clickTarget != null && PointerTargetEnabled(clickTarget, captured) && (ActivationTarget(clickTarget) is not { } activation || PointerTargetEnabled(activation, captured)))
            {
                Dispatch(clickTarget, new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true, Composed = true, ClientX = x, ClientY = y, Detail = 1, PointerId = MousePointerId, PointerType = "mouse", IsPrimary = false }));
                if (ActivationTarget(clickTarget) != null) PointerActivationCount++;
            }
            if (_disposed || version != _inputVersion) return;
            var physical = HitTarget(x, y);
            UpdatePointerBoundary(physical, x, y, 0, 0);
            UpdateMouseBoundary(physical, x, y, 0);
        }
    }
    private void RequireActivePointer(int pointerId)
    {
        if (_disposed || !_pointerKnown || pointerId != MousePointerId)
            throw DomException.NotFound("No active pointer with the supplied pointerId.");
    }
    internal void SetPointerCapture(Element element, int pointerId)
    {
        RequireActivePointer(pointerId);
        if (!element.IsConnected) throw DomException.InvalidState("The capturing element is not connected.");
        if (_pointerButtons == 0 || !ReferenceEquals(element.NodeDocument, _document)) return;
        _pendingCaptureTarget = element;
    }
    internal void ReleasePointerCapture(Element element, int pointerId)
    {
        RequireActivePointer(pointerId);
        if (ReferenceEquals(element, _pendingCaptureTarget)) _pendingCaptureTarget = null;
    }
    internal bool HasPointerCapture(Element element, int pointerId) => !_disposed && _pointerKnown && pointerId == MousePointerId && ReferenceEquals(element, _pendingCaptureTarget);
    internal void PointerSubtreeRemoved(Node subtree)
    {
        if (_pendingCaptureTarget != null && subtree.IsShadowIncludingInclusiveAncestorOf(_pendingCaptureTarget)) _pendingCaptureTarget = null;
        if (_captureTarget is Element capture && subtree.IsShadowIncludingInclusiveAncestorOf(capture)) _captureTarget = _document;
        if (_hover != null && subtree.IsShadowIncludingInclusiveAncestorOf(_hover))
        {
            _hover.PseudoState &= ~ElementPseudoState.Hover;
            _hover = subtree.ParentElement;
            if (_hover != null) _hover.PseudoState |= ElementPseudoState.Hover;
        }
        if (_mouseHover != null && subtree.IsShadowIncludingInclusiveAncestorOf(_mouseHover)) _mouseHover = subtree.ParentElement;
    }
    private void RecordPointer(double x, double y, int buttons)
    { _pointerKnown = true; _pointerX = x; _pointerY = y; _pointerButtons = buttons; }
    private PointerEvent CaptureEvent(string type, double x, double y, int buttons, int button) => new(type,
        new PointerEventInit { Bubbles = true, Composed = true, ClientX = x, ClientY = y, Buttons = buttons, Button = button, PointerId = MousePointerId, PointerType = "mouse", IsPrimary = true });
    private Element? ProcessPendingCapture(double x, double y, int buttons, int button, out bool detachedDuringGot)
    {
        detachedDuringGot = false;
        if (_processingCapture) return _captureTarget as Element;
        int version = _inputVersion;
        EventTarget? previous = _captureTarget;
        Element? pending = _pendingCaptureTarget;
        if (ReferenceEquals(previous, pending)) return previous as Element;
        _processingCapture = true;
        try
        {
            // Snapshot the requested override. Changes made by capture listeners are pending for the
            // next pointer event, but a disconnected requested target must never receive gotcapture.
            _captureTarget = null;
            if (previous != null) previous.DispatchUserEvent(CaptureEvent("lostpointercapture", x, y, buttons, button));
            if (_disposed || version != _inputVersion || pending == null || !ReferenceEquals(pending.NodeDocument, _document)) return null;
            UpdatePointerBoundary(pending, x, y, buttons, button);
            if (_disposed || version != _inputVersion) return null;
            bool disconnectedRequest = !pending.IsConnected;
            if (!disconnectedRequest)
            {
                _captureTarget = pending;
                pending.DispatchUserEvent(CaptureEvent("gotpointercapture", x, y, buttons, button));
                // Removal during gotcapture replaces the current override with the document.
                // Loss resolves now, but this in-flight event retains its detached target.
                if (ReferenceEquals(_captureTarget, _document))
                {
                    detachedDuringGot = true;
                    _captureTarget = null;
                    _document.DispatchUserEvent(CaptureEvent("lostpointercapture", x, y, buttons, button));
                    if (_hover != null) _hover.PseudoState &= ~ElementPseudoState.Hover;
                    _hover = pending;
                }
            }
            // Boundary transitions retain the snapshotted related target when lost/got handlers
            // detach it; events on the detached node cannot bubble into the document.
            if (!_disposed && version == _inputVersion) UpdateMouseBoundary(pending, x, y, buttons);
            // A requested target detached by lost/gotcapture still owns this in-flight event,
            // without receiving a new gotcapture or retaining capture for later native events.
            return _disposed || version != _inputVersion ? null : !pending.IsConnected ? pending : _captureTarget as Element;
        }
        finally { _processingCapture = false; }
    }
    /// <summary>UA stream cancellation at native modal, context, window-focus and destruction edges.</summary>
    public void PointerCancel()
    {
        if (_disposed || _cancellingPointer) return;
        ClearScrollbarHover();
        _textDrag = null;
        if (!_pointerDown && _pointerButtons == 0)
        {
            var capture = _captureTarget; _captureTarget = null; _pendingCaptureTarget = null;
            bool interrupted = capture != null || _pointerDownTarget != null;
            ResetPress(); _pointerDownTarget = null; _suppressMouse = false;
            if (interrupted) _inputVersion++;
            capture?.DispatchUserEvent(CaptureEvent("lostpointercapture", _pointerX, _pointerY, 0, -1));
            return;
        }
        _cancellingPointer = true;
        _inputVersion++; _pointerDown = false; _pointerButtons = 0;
        try
        {
            var eventCapture = ProcessPendingCapture(_pointerX, _pointerY, 0, -1, out _);
            var target = eventCapture ?? _captureTarget ?? (EventTarget?)HitTarget(_pointerX, _pointerY) ?? _pressed;
            target?.DispatchUserEvent(CaptureEvent("pointercancel", _pointerX, _pointerY, 0, -1));
            UpdatePointerBoundary(null, _pointerX, _pointerY, 0, -1);
            var capture = _captureTarget; _captureTarget = null; _pendingCaptureTarget = null;
            capture?.DispatchUserEvent(CaptureEvent("lostpointercapture", _pointerX, _pointerY, 0, -1));
            UpdateMouseBoundary(null, _pointerX, _pointerY, 0);
            ResetPress(); _pointerDownTarget = null; _suppressMouse = false;
        }
        finally { _cancellingPointer = false; }
    }
}
