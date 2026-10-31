#nullable enable
using System;
using Godot;
using NecronomiDom.Dom;
using DomKeyboardEvent = NecronomiDom.Dom.KeyboardEvent;
using InputEvent = Godot.InputEvent;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>Input action that activates the focused element (joypad confirm; keyboard Enter/Space arrive as DOM keys).</summary>
    [Export] public StringName SubmitAction { get; set; } = "ui_accept";
    /// <summary>Input action that raises <see cref="CancelRequested"/>; Escape also raises it unless the DOM cancels the key.</summary>
    [Export] public StringName CancelAction { get; set; } = "ui_cancel";
    /// <summary>Directional actions that move document focus between focusable elements.</summary>
    [Export] public StringName NavigateLeftAction { get; set; } = "ui_left";
    [Export] public StringName NavigateRightAction { get; set; } = "ui_right";
    [Export] public StringName NavigateUpAction { get; set; } = "ui_up";
    [Export] public StringName NavigateDownAction { get; set; } = "ui_down";
    /// <summary>Raised once per cancel press (Escape or <see cref="CancelAction"/>) while the host accepts input.
    /// Closing screens, popping menus or vetoing the intent belongs to the application.</summary>
    public event Action? CancelRequested;
    private StringName NavLeft => NavigateLeftAction;
    private StringName NavRight => NavigateRightAction;
    private StringName NavUp => NavigateUpAction;
    private StringName NavDown => NavigateDownAction;
    private Godot.Window? _inputWindow;
    private bool _inputEnabled = true, _submitDown, _cancelDown, _waitForRelease, _nativeGuiPress;
    private Vector2 _navigation;
    private double _repeatRemaining;
    private static readonly double InitialRepeatSeconds = RepeatCadence.Period(0.4), RepeatSeconds = RepeatCadence.Period(0.1);
    private bool _wasAvailable;
    /// <summary>The application owns directional navigation for this host: navigate actions do not move document
    /// focus, and physical arrow keys are not reserved for scrolling.</summary>
    public bool ApplicationNavigation { get; set; }
    /// <summary>Owners suspend a host here (or make its document root inert), e.g. while a modal covers it.</summary>
    public bool InputEnabled
    {
        get => _inputEnabled;
        set
        {
            if (_inputEnabled == value) return;
            _inputEnabled = value; ResetInput();
            if (!value)
            {
                _documentInput?.ReleaseInput();
                // A parent screen exits after this child host. DOM cleanup still runs,
                // but native focus was already withdrawn when the host left the tree.
                if (IsInsideTree()) ReleaseFocus();
            }
        }
    }
    private bool Available
    {
        get
        {
            return InputEnabled && IsVisibleInTree()
                && Document?.DocumentElement is { } root && !NecronomiDom.Dom.Document.IsInert(root);
        }
    }
    private void InitializeInput()
    {
        FocusExited += LostHostFocus;
        _inputWindow = GetWindow();
        _inputWindow.FocusExited += LostHostFocus;
        MouseExited += PointerExited;
        Godot.Input.JoyConnectionChanged += NavigationControllerConnectionChanged;
    }
    private void DisposeInput()
    {
        FocusExited -= LostHostFocus;
        if (_inputWindow != null) { _inputWindow.FocusExited -= LostHostFocus; _inputWindow = null; }
        MouseExited -= PointerExited;
        Godot.Input.JoyConnectionChanged -= NavigationControllerConnectionChanged;
        ResetInput();
        _wasAvailable = false;
    }
    private void LostHostFocus() { ResetInput(); _documentInput?.ReleaseInput(); }
    private void PointerExited()
    {
        // Godot 4.7.2 emits this coordinate-less signal before _Input. A held native GUI grab
        // supplies the authoritative outside motion/up to _GuiInput; it is not DOM capture.
        if (!Available || !GetWindow().HasFocus() || _nativeGuiPress || _documentInput?.HasPointerCaptureOverride == true) return;
        Vector2 point = GetLocalMousePosition();
        _documentInput?.PointerExit(point.X, point.Y, (int)Godot.Input.GetMouseButtonMask());
    }
    private void ResetInput()
    {
        _documentInput?.ReleaseInput(false);
        _submitDown = false; _cancelDown = false; _navigation = Vector2.Zero; _repeatRemaining = 0;
        _nativeGuiPress = false;
        _controllerNavigation.Clear();
        _waitForRelease = true;
    }
    private static bool Held(StringName action) => InputMap.HasAction(action) && Godot.Input.IsActionPressed(action);
    private static float Strength(StringName action) => InputMap.HasAction(action) ? Godot.Input.GetActionStrength(action) : 0;
    private Vector2 ReadNavigation() => new(Strength(NavRight) - Strength(NavLeft), Strength(NavUp) - Strength(NavDown));
    private void ProcessDocumentInput(double delta)
    {
        bool available = Available;
        if (!available)
        {
            if (_wasAvailable) LostHostFocus();
            _wasAvailable = false; return;
        }
        _wasAvailable = true;
        _documentInput?.ValidateTransientState();
        Vector2 navigation = ReadNavigation();
        if (_waitForRelease)
        {
            if (!Held(SubmitAction) && !Held(CancelAction) && navigation.LengthSquared() < 0.25f) _waitForRelease = false;
            return;
        }
        if (!HasFocus()) return;
        if (ApplicationNavigation) return;
        if (Document.FocusedElement is ITextControl) navigation = ReadControllerNavigation();
        if (Document.FocusedElement is ITextControl && (Godot.Input.IsPhysicalKeyPressed(Key.Left)
            || Godot.Input.IsPhysicalKeyPressed(Key.Right) || Godot.Input.IsPhysicalKeyPressed(Key.Up) || Godot.Input.IsPhysicalKeyPressed(Key.Down))) return;
        if (!ApplicationNavigation && _documentInput?.HasUserScrollAncestor(Document.FocusedElement) == true && (Godot.Input.IsPhysicalKeyPressed(Key.Left)
            || Godot.Input.IsPhysicalKeyPressed(Key.Right) || Godot.Input.IsPhysicalKeyPressed(Key.Up) || Godot.Input.IsPhysicalKeyPressed(Key.Down))) return;
        Vector2 cardinal = navigation.LengthSquared() < 0.25f ? Vector2.Zero : Math.Abs(navigation.X) > Math.Abs(navigation.Y) ? new Vector2(Math.Sign(navigation.X), 0) : new Vector2(0, Math.Sign(navigation.Y));
        if (cardinal == Vector2.Zero) { _navigation = cardinal; return; }
        _repeatRemaining -= delta;
        if (cardinal != _navigation || RepeatCadence.Due(0, _repeatRemaining))
        {
            // Directional repeat: 400 ms initial / 100 ms subsequent, driven by unscaled process time and cleared
            // at context edges. The repeat carries its overshoot instead of restarting from the firing frame, so
            // the cadence holds at every frame rate.
            _repeatRemaining = cardinal != _navigation ? InitialRepeatSeconds : RepeatCadence.Next(_repeatRemaining, RepeatSeconds, 0);
            _navigation = cardinal;
            _documentInput!.Navigate(cardinal.X < 0 ? "left" : cardinal.X > 0 ? "right" : cardinal.Y > 0 ? "up" : "down");
        }
    }
    public override void _Input(InputEvent input)
    {
        // DOM/modal callbacks may synchronously withdraw this host. Consume on the borrowed
        // viewport and commit press state before dispatch; teardown's ResetInput must win.
        var viewport = GetViewport();
        if (WheelObserved != null && input is InputEventMouseButton
            { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight } observedWheel)
            ObserveWheel("input", observedWheel, GetGlobalTransformWithCanvas().AffineInverse() * observedWheel.Position);
        if (Available && _documentInput?.HasPointerCaptureOverride == true)
        {
            // _Input sees viewport coordinates before Control's GUI grab/hit search. Explicit DOM
            // capture therefore also works outside this host, and handling here prevents GUI replay.
            if (input is InputEventMouseMotion motion)
            {
                Vector2 point = GetGlobalTransformWithCanvas().AffineInverse() * motion.Position;
                viewport.SetInputAsHandled();
                _documentInput.PointerMove(point.X, point.Y, (int)motion.ButtonMask); return;
            }
            // A GUI-originated press has a native grab. Let its release reach _GuiInput so Godot
            // clears both grab owner and mask; handling up in _Input only clears the mask in 4.7.2.
            // Capture begun while moving a held pointer from the world has no such native grab.
            if (!_nativeGuiPress && input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
            {
                Vector2 point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
                viewport.SetInputAsHandled();
                _documentInput.PointerButton(point.X, point.Y, mouse.Pressed); return;
            }
        }
        if (!Available || !HasFocus() || input is not (InputEventJoypadButton or InputEventJoypadMotion or InputEventAction)) return;
        ObserveControllerNavigation(input);
        if (input.IsAction(SubmitAction))
        {
            bool pressed = input.IsPressed(), submit = !_waitForRelease && pressed && !_submitDown;
            _submitDown = pressed; viewport.SetInputAsHandled();
            if (submit) _documentInput?.Submit();
        }
        else if (input.IsAction(CancelAction))
        {
            bool pressed = input.IsPressed(), cancel = !_waitForRelease && pressed && !_cancelDown;
            _cancelDown = pressed; viewport.SetInputAsHandled();
            if (cancel) CancelIntent();
        }
        else if (input.IsAction(NavLeft) || input.IsAction(NavRight) || input.IsAction(NavUp) || input.IsAction(NavDown))
            viewport.SetInputAsHandled(); // Process polls the navigation vector once, including repeated holds.
    }
    // Form submission/dialog/popover watchers are not supported by this runtime; one press is one intent.
    private void CancelIntent() => CancelRequested?.Invoke();
    public override void _GuiInput(InputEvent input)
    {
        var viewport = GetViewport();
        if (WheelObserved != null && input is InputEventMouseButton
            { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight } observedWheel)
            ObserveWheel("gui", observedWheel, observedWheel.Position);
        bool ownedRelease = input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } && _nativeGuiPress;
        if (!Available || _documentInput == null)
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) { _nativeGuiPress = false; viewport.SetInputAsHandled(); }
            return;
        }
        var documentInput = _documentInput;
        if (input is InputEventMouseMotion motion)
        { viewport.SetInputAsHandled(); documentInput.PointerMove(motion.Position.X, motion.Position.Y, (int)motion.ButtonMask); }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        {
            viewport.SetInputAsHandled();
            if (mouse.Pressed) { _nativeGuiPress = true; GrabFocus(); }
            else if (ownedRelease) _nativeGuiPress = false;
            documentInput.PointerButton(mouse.Position.X, mouse.Position.Y, mouse.Pressed);
        }
        else if (input is InputEventMouseButton { Pressed: true } wheel && HandleScrollWheel(wheel)) viewport.SetInputAsHandled();
        else if (input is InputEventKey key)
        {
            // Including Tab/arrow edges: Godot focus search must never run inside a document.
            viewport.SetInputAsHandled();
            if (_waitForRelease) return;
            var translated = TranslateKey(key);
            string text = key.Unicode > 0 ? char.ConvertFromUtf32((int)key.Unicode) : "";
            bool cancel = translated.Key == "Escape" && key.Pressed && !key.Echo && !_cancelDown;
            if (translated.Key == "Escape") _cancelDown = key.Pressed;
            bool allowed = documentInput.Key(translated, text);
            if (cancel && allowed) CancelIntent();
        }
    }
    private static DomKeyboardEvent TranslateKey(InputEventKey input)
    {
        Key physical = input.PhysicalKeycode != Key.None ? input.PhysicalKeycode : input.Keycode;
        Key logical = input.Keycode != Key.None ? input.Keycode : physical;
        string key = logical switch
        {
            Key.Tab => "Tab", Key.Backtab => "Tab", Key.Enter or Key.KpEnter => "Enter", Key.Space => " ", Key.Escape => "Escape",
            Key.Left => "ArrowLeft", Key.Right => "ArrowRight", Key.Up => "ArrowUp", Key.Down => "ArrowDown",
            Key.Shift => "Shift", Key.Ctrl => "Control", Key.Alt => "Alt", Key.Meta => "Meta", Key.Backspace => "Backspace",
            Key.Delete => "Delete", Key.Home => "Home", Key.End => "End", Key.Pageup => "PageUp", Key.Pagedown => "PageDown",
            _ => input.Unicode >= 0x20 ? char.ConvertFromUtf32((int)input.Unicode) : logical >= Key.A && logical <= Key.Z ? (input.ShiftPressed ? (char)logical : char.ToLowerInvariant((char)logical)).ToString() : "Unidentified"
        };
        string code = physical >= Key.A && physical <= Key.Z ? "Key" + (char)physical : physical >= Key.Key0 && physical <= Key.Key9 ? "Digit" + (char)physical : physical switch
        {
            Key.Left => "ArrowLeft", Key.Right => "ArrowRight", Key.Up => "ArrowUp", Key.Down => "ArrowDown", Key.Backtab => "Tab",
            Key.Enter => "Enter", Key.KpEnter => "NumpadEnter", Key.Space => "Space", Key.Escape => "Escape", Key.Tab => "Tab",
            Key.Ctrl => input.Location == KeyLocation.Right ? "ControlRight" : "ControlLeft",
            Key.Shift => input.Location == KeyLocation.Right ? "ShiftRight" : "ShiftLeft",
            Key.Alt => input.Location == KeyLocation.Right ? "AltRight" : "AltLeft",
            Key.Meta => input.Location == KeyLocation.Right ? "MetaRight" : "MetaLeft", _ => physical.ToString()
        };
        return new DomKeyboardEvent(input.Pressed ? "keydown" : "keyup", new KeyboardEventInit
        { Bubbles = true, Cancelable = true, Composed = true, Key = key, Code = code, Repeat = input.Echo,
            ShiftKey = input.ShiftPressed || logical == Key.Backtab, CtrlKey = input.CtrlPressed, AltKey = input.AltPressed, MetaKey = input.MetaPressed });
    }
}
