#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using DomElement = NecronomiDom.Dom.Element;
using DomDocument = NecronomiDom.Dom.Document;
using PaintRect = NecronomiDom.Paint.Rect;

namespace NecronomiDom.Runtime;

/// <summary>Explicitly attached, debug-only native tooling overlay (not document UI).
/// Closed inspectors have no process/input callback or measurement subscription. All queries run on the UI thread.</summary>
[GodotClass]
public partial class DocumentInspector : CanvasLayer
{
    private DocumentHost? _host;
    private Control _shield = null!;
    private PanelContainer _dock = null!;
    private Label _title = null!, _pickHint = null!;
    private ItemList _ancestry = null!;
    private LineEdit _selector = null!;
    private TextEdit _boxes = null!, _rules = null!, _computed = null!, _variables = null!, _state = null!;
    private IReadOnlyList<DomElement> _ancestors = Array.Empty<DomElement>();
    private DomElement? _hovered;
    private DomDocument? _observedDocument, _savedDocument;
    private HTMLElement? _savedFocus;
    private Control? _savedNativeFocus;
    private bool _savedInput, _savedHostFocus, _savedFocusVisible, _picking, _built;
    private double _elapsed;
    private DocumentInputInspection? _savedPointerState;
    private DocumentUpdateTiming? _lastTiming;
    public bool IsOpen { get; private set; }
    public DomElement? SelectedElement { get; private set; }
    public string Report { get; private set; } = "";

    internal void Attach(DocumentHost host) => _host = host;

    public override void _Ready()
    {
        Layer = 1000;
        ProcessMode = ProcessModeEnum.Always;
        BuildControls();
        SetProcess(false);
        SetProcessInput(false);
        _shield.Hide();
    }

    public void Open()
    {
        if (IsOpen) return;
        if (!OS.IsDebugBuild()) throw new InvalidOperationException("The DOM inspector requires a debug engine/template.");
        if (_host == null || !IsInsideTree() || !_built)
            throw new InvalidOperationException("Attach the inspector to a host in the scene tree before opening it.");
        _savedInput = _host.InputEnabled;
        _savedHostFocus = _host.HasFocus();
        _savedNativeFocus = GetViewport().GuiGetFocusOwner();
        _savedDocument = _host.Document;
        _savedFocus = _host.Document?.FocusedElement;
        _savedFocusVisible = _host.Document?.FocusVisible ?? false;
        _savedPointerState = _host.InspectInputState();
        _observedDocument = _host.Document;
        IsOpen = true;
        // Withdraw DOM press/capture and native focus before the shield can receive any input.
        _host.InputEnabled = false;
        _host.UpdateMeasured += ObserveUpdate;
        _lastTiming = null;
        _elapsed = 0;
        _shield.Show();
        _dock.Show();
        _pickHint.Hide();
        _shield.GrabFocus();
        SetProcess(true);
        SetProcessInput(true);
        if (SelectedElement == null || !SelectedElement.IsConnected || !ReferenceEquals(SelectedElement.OwnerDocument, _host.Document))
            SelectedElement = _host.Document?.DocumentElement;
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        SetProcess(false);
        SetProcessInput(false);
        _picking = false;
        _hovered = null;
        _shield.Hide();
        if (_host != null && GodotObject.IsInstanceValid(_host))
        {
            _host.UpdateMeasured -= ObserveUpdate;
            _host.InputEnabled = _savedInput;
            if (_savedInput && ReferenceEquals(_host.Document, _savedDocument) && _savedFocus?.IsConnected == true)
                _host.Document.Focus(_savedFocus, _savedFocusVisible, preventScroll: true);
            if (_host.IsInsideTree() && _savedInput && _savedHostFocus) _host.GrabFocus();
        }
        if (!_savedHostFocus && _savedNativeFocus != null && GodotObject.IsInstanceValid(_savedNativeFocus)
            && _savedNativeFocus.IsInsideTree() && _savedNativeFocus.IsVisibleInTree()) _savedNativeFocus.GrabFocus();
        _savedNativeFocus = null;
        _savedFocus = null;
        _savedDocument = null;
        _lastTiming = null;
        _savedPointerState = null;
        _observedDocument = null;
        SelectedElement = null;
        _ancestors = Array.Empty<DomElement>();
        _ancestry.Clear();
    }

    public override void _ExitTree() => Close();

    public void BeginPick()
    {
        if (!IsOpen) Open();
        _picking = true;
        _dock.Hide();
        _pickHint.Show();
        _shield.GrabFocus();
        _shield.QueueRedraw();
    }

    public bool Pick(double x, double y)
    {
        if (_host?.Document == null) return false;
        _host.FlushPendingLayout();
        DomElement? element = DocumentInspection.Pick(_host.DisplayList, x, y);
        if (element == null) return false;
        Select(element);
        return true;
    }

    public void Select(DomElement element)
    {
        if (_host == null || !ReferenceEquals(element.OwnerDocument, _host.Document) || !element.IsConnected)
            throw new ArgumentException("Select a connected element in the attached host.", nameof(element));
        SelectedElement = element;
        _hovered = null;
        FinishPick();
        if (IsOpen) Refresh();
    }

    private void FinishPick()
    {
        _picking = false;
        _pickHint.Hide();
        _dock.Show();
        _shield.QueueRedraw();
    }

    public override void _Input(Godot.InputEvent input)
    {
        if (!IsOpen) return;
        if (input is InputEventKey { Keycode: Key.Escape } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.Pressed && !key.Echo) { if (_picking) FinishPick(); else Close(); }
            return;
        }
        if (!_picking || _host == null || input is not (InputEventMouseMotion or InputEventMouseButton)) return;
        // Handle at viewport input stage: no native GUI grab and no DOM press are created by a pick.
        GetViewport().SetInputAsHandled();
        Vector2 position = input is InputEventMouseMotion motion ? motion.Position : ((InputEventMouseButton)input).Position;
        Vector2 point = _host.GetGlobalTransformWithCanvas().AffineInverse() * position;
        _host.FlushPendingLayout();
        _hovered = DocumentInspection.Pick(_host.DisplayList, point.X, point.Y);
        _pickHint.Text = "Pick: " + DocumentInspection.Label(_hovered) + " — left click selects; Escape cancels";
        _shield.QueueRedraw();
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            if (_hovered != null) Select(_hovered);
            else FinishPick();
        }
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || _host == null) return;
        if (!GodotObject.IsInstanceValid(_host) || !_host.IsInsideTree()) { Close(); return; }
        _elapsed += delta;
        // Live style/text/focus changes need not produce a layout revision. Poll only while open,
        // bounded to 10 Hz; geometry highlights follow the same refreshed renderer snapshot.
        if (_elapsed < 0.1) return;
        _elapsed = 0;
        if (_host.InputEnabled) _host.InputEnabled = false;
        Refresh();
    }

    private void ObserveUpdate(DocumentUpdateTiming timing) => _lastTiming = timing;

    private void BuildControls()
    {
        _shield = new Control { Name = "DOMInspectorShield", MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.All };
        AddChild(_shield);
        _shield.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _shield.Draw += DrawHighlight;
        _shield.GuiInput += _ => _shield.AcceptEvent();
        _dock = new PanelContainer { Name = "DOMInspectorDock" };
        _dock.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.075f, 0.065f, 1),
            BorderColor = new Color(0.22f, 0.29f, 0.25f, 1),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        });
        _shield.AddChild(_dock);
        _dock.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        _dock.OffsetLeft = -540; _dock.OffsetRight = -12; _dock.OffsetTop = 12; _dock.OffsetBottom = -12;
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 10);
        _dock.AddChild(margin);
        var column = new VBoxContainer(); margin.AddChild(column);
        _title = new Label { Text = "NecronomiDOM · live inspector" }; column.AddChild(_title);
        var buttons = new HBoxContainer(); column.AddChild(buttons);
        AddButton(buttons, "Pick element", BeginPick);
        AddButton(buttons, "Parent", () => { if (_ancestors.Count > 1) Select(_ancestors[1]); });
        AddButton(buttons, "Close", Close);
        _selector = new LineEdit { PlaceholderText = "Select by CSS selector and press Enter" }; column.AddChild(_selector);
        _selector.TextSubmitted += selector =>
        {
            try
            {
                DomElement? element = _host?.Document?.QuerySelector(selector);
                if (element != null) Select(element);
                else _title.Text = "No matching element";
            }
            catch (DomException error) { _title.Text = error.Message; }
        };
        _ancestry = new ItemList { CustomMinimumSize = new Vector2(0, 90) }; column.AddChild(_ancestry);
        _ancestry.ItemSelected += index => { if (index >= 0 && index < _ancestors.Count) Select(_ancestors[(int)index]); };
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; column.AddChild(tabs);
        _boxes = AddTextTab(tabs, "Boxes");
        _rules = AddTextTab(tabs, "Rules");
        _computed = AddTextTab(tabs, "Computed");
        _variables = AddTextTab(tabs, "Variables");
        _state = AddTextTab(tabs, "State + metrics");
        _pickHint = new Label { Text = "Left click an element; Escape cancels", Position = new Vector2(12, 12), MouseFilter = Control.MouseFilterEnum.Ignore };
        _pickHint.AddThemeColorOverride("font_color", new Color(1, 1, 0.4f));
        _pickHint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 1));
        _pickHint.AddThemeConstantOverride("shadow_offset_x", 2);
        _pickHint.AddThemeConstantOverride("shadow_offset_y", 2);
        _shield.AddChild(_pickHint);
        _shield.Resized += FitDock;
        _built = true;
        FitDock();
    }

    private void FitDock()
    {
        _dock.OffsetLeft = -Math.Min(540, Math.Max(240, _shield.Size.X - 24));
    }

    private static void AddButton(HBoxContainer parent, string text, Action action)
    {
        var button = new Button { Text = text }; button.Pressed += action; parent.AddChild(button);
    }

    private static TextEdit AddTextTab(TabContainer tabs, string title)
    {
        var text = new TextEdit { Name = title, Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary };
        text.AddThemeFontSizeOverride("font_size", 13);
        tabs.AddChild(text);
        return text;
    }

    private void Refresh()
    {
        if (_host == null) return;
        _host.FlushPendingLayout();
        if (!ReferenceEquals(_observedDocument, _host.Document))
        {
            _observedDocument = _host.Document;
            SelectedElement = _host.Document?.DocumentElement;
            _hovered = null;
        }
        if (SelectedElement?.IsConnected != true) SelectedElement = _host.Document?.DocumentElement;
        DomElement? selected = SelectedElement;
        _title.Text = "NecronomiDOM · " + DocumentInspection.Label(selected);
        RefreshAncestry(selected);
        string boxes = BuildBoxes(selected);
        string state = BuildState(selected);
        string rules = "No loaded document.", computed = "", variables = "";
        if (selected != null && _host.Styles != null)
        {
            StyleInspection inspection = _host.Styles.Inspect(selected);
            rules = BuildRules(inspection);
            var values = new StringBuilder();
            foreach (var property in inspection.Computed) values.Append(property.Name).Append(": ").AppendLine(property.Value);
            computed = values.ToString();
            variables = BuildVariables(inspection);
        }
        SetText(_boxes, boxes); SetText(_rules, rules); SetText(_computed, computed); SetText(_variables, variables); SetText(_state, state);
        Report = _title.Text + "\n\nANCESTRY\n" + AncestryText() + "\nBOXES\n" + boxes + "\nRULES\n" + rules
            + "\nCOMPUTED\n" + computed + "\nCUSTOM PROPERTIES\n" + variables + "\nSTATE + METRICS\n" + state;
        _shield.QueueRedraw();
    }

    /// <summary>Refresh synchronously for explicit tooling queries; ordinary UI refresh is bounded to 10 Hz.</summary>
    public string CaptureReport()
    {
        if (IsOpen) Refresh();
        return Report;
    }

    private void RefreshAncestry(DomElement? selected)
    {
        IReadOnlyList<DomElement> next = selected == null ? Array.Empty<DomElement>() : DocumentInspection.Ancestry(selected);
        bool changed = next.Count != _ancestors.Count;
        if (!changed) for (int i = 0; i < next.Count; i++) if (!ReferenceEquals(next[i], _ancestors[i])) { changed = true; break; }
        if (!changed) return;
        _ancestors = next;
        _ancestry.Clear();
        foreach (DomElement ancestor in next) _ancestry.AddItem(DocumentInspection.Label(ancestor));
        if (next.Count > 0) _ancestry.Select(0);
    }

    private string AncestryText()
    {
        var text = new StringBuilder();
        foreach (var ancestor in _ancestors) text.AppendLine(DocumentInspection.Label(ancestor));
        return text.ToString();
    }

    private string BuildBoxes(DomElement? selected)
    {
        var text = new StringBuilder("Document-local CSS pixels. Outline = visual AABB (not a clipped polygon).\n\n");
        if (selected?.SourceLocation is { } location)
            text.Append("HTML start token: ").Append(_host!.DocumentPath).Append(':').Append(location.Line).Append(':').AppendLine(location.Column.ToString(CultureInfo.InvariantCulture));
        else text.AppendLine("HTML start-token location unavailable (generated/mutated DOM).");
        int count = 0;
        foreach (var box in _host!.DisplayList.Boxes)
        {
            if (!ReferenceEquals(box.Element, selected)) continue;
            text.Append("Box ").Append(++count).Append(" logical border bounds: ").AppendLine(Format(box.Bounds));
            text.Append("  Visual AABB: ").AppendLine(Format(box.VisualBounds));
            text.Append("  DOM pointer events: ").AppendLine(box.PointerEvents ? "enabled" : "none (still inspectable)");
        }
        if (count == 0) text.AppendLine("No rendered layout box (e.g. display:none/display:contents). Select a rendered ancestor/descendant.");
        return text.ToString();
    }

    private static string Format(PaintRect rect) => FormattableString.Invariant($"x={rect.X:0.##} y={rect.Y:0.##} width={rect.Width:0.##} height={rect.Height:0.##}");

    private static string BuildRules(StyleInspection inspection)
    {
        var text = new StringBuilder("Matched CSSOM rules, expanded longhands. WIN = cascaded winner after revert/revert-layer; not a promise of computed validity.\nCSSOM rule/declaration line and column unavailable. Rule text reflects live CSSOM edits.\n\n");
        foreach (var source in inspection.Sources)
        {
            text.Append('[').Append(source.Order).Append("] ").Append(source.SourcePath).Append(" · ").AppendLine(source.Selector);
            text.AppendLine(source.RuleText).AppendLine();
        }
        text.AppendLine("CASCADE (strongest declaration first per property)");
        foreach (var declaration in inspection.Declarations)
        {
            var priority = declaration.Priority;
            text.Append(declaration.Winner ? "WIN " : "    ").Append(declaration.Property).Append(": ").Append(declaration.Value);
            if (priority.Important) text.Append(" !important");
            text.Append("  [").Append(priority.Order == long.MinValue ? "HTML presentational hint" : priority.Order.ToString(CultureInfo.InvariantCulture)).Append("] ")
                .Append(priority.Origin).Append(" layer=").Append(priority.Layer).Append(" specificity=")
                .Append(priority.Specificity.A).Append(',').Append(priority.Specificity.B).Append(',').Append(priority.Specificity.C);
            if (priority.Inline) text.Append(" inline");
            if (declaration.Property != declaration.DeclaredProperty) text.Append(" from ").Append(declaration.DeclaredProperty);
            text.AppendLine();
        }
        text.AppendLine("Properties without a local winner use inheritance or their initial value; see Computed and Variables. Animation samples are renderer state, not additional CSSOM rules.");
        return text.ToString();
    }

    private static string BuildVariables(StyleInspection inspection)
    {
        var text = new StringBuilder("Custom-property inputs and final computed results at this element. Inherited input is already the parent's computed value; registered properties may provide an initial value.\nInvalid/missing variables use var() fallbacks at each consumer (if supplied). Empty values are valid.\n\n");
        foreach (var property in inspection.CustomProperties)
        {
            text.Append(property.Name).Append(property.LocallyDeclared ? " [local cascade]" : " [inherited / registered initial / referenced]").AppendLine();
            text.Append("  raw input: ").AppendLine(property.Raw);
            text.Append("  resolved: ").AppendLine(property.Resolved == null ? "<guaranteed-invalid or missing>" : property.Resolved.Length == 0 ? "<valid empty value>" : property.Resolved);
        }
        text.AppendLine("CONSUMERS (authored expressions → computed results)");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in inspection.Computed) values[property.Name] = property.Value;
        foreach (var declaration in inspection.Declarations)
            if (declaration.Winner && declaration.Value.Contains("var(", StringComparison.OrdinalIgnoreCase))
                text.Append(declaration.Property).Append(": ").Append(declaration.Value).Append(" → ").AppendLine(values.GetValueOrDefault(declaration.Property, "<invalid/missing>"));
        return text.ToString();
    }

    private string BuildState(DomElement? selected)
    {
        var host = _host!;
        var text = new StringBuilder();
        text.Append("Input: suspended by inspector; prior enabled=").AppendLine(_savedInput.ToString());
        text.Append("Current host InputEnabled: ").AppendLine(host.InputEnabled.ToString());
        text.Append("DOM focus now: ").AppendLine(DocumentInspection.Label(host.Document?.FocusedElement));
        text.Append("Focus before open: ").AppendLine(DocumentInspection.Label(_savedFocus));
        text.Append("DOM focus-visible: ").AppendLine((host.Document?.FocusVisible ?? false).ToString());
        text.Append("Selected pseudo-state: ").AppendLine(selected?.PseudoState.ToString() ?? "<none>");
        text.Append("Native focused control: ").AppendLine(GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "<none>");
        text.Append("Application navigation: ").AppendLine(host.ApplicationNavigation.ToString());
        text.Append("Pointer activations: ").AppendLine(host.PointerActivationCount.ToString(CultureInfo.InvariantCulture));
        if (_savedPointerState is { } priorPointer) AppendPointerState(text, "Before open", priorPointer);
        if (host.InspectInputState() is { } currentPointer) AppendPointerState(text, "Current", currentPointer);
        if (selected is ITextControl control)
        {
            var selection = control.EditorSelection;
            text.Append("Text control: length=").Append(control.Value.Length).Append(" readOnly=").AppendLine(control.ReadOnly.ToString());
            text.Append("Selection (UTF-16): ").Append(selection.Start).Append("..").Append(selection.End).Append(' ').AppendLine(selection.Direction);
        }
        text.AppendLine().AppendLine("HOST METRICS (not GPU or whole-frame timings)");
        text.Append("Layout revision: ").Append(host.LayoutRevision).Append("  paint revision: ").AppendLine(host.PaintRevision.ToString(CultureInfo.InvariantCulture));
        text.Append("Display boxes: ").Append(host.DisplayList.Boxes.Count).Append("  paint runs: ").AppendLine(host.DisplayList.Runs.Count.ToString(CultureInfo.InvariantCulture));
        text.Append("Painted boxes: ").Append(host.PaintedBoxCount).Append("  text: ").Append(host.PaintedTextRunCount).Append("  images: ").AppendLine(host.PaintedImageRunCount.ToString(CultureInfo.InvariantCulture));
        text.Append("Shaped runs: ").Append(host.ShapedRunCount).Append("  backend: ").AppendLine(host.TextBackend);
        if (_lastTiming is { } timing)
        {
            text.AppendLine(FormattableString.Invariant($"Latest host update: total={timing.TotalMs:0.###}ms reconcile={timing.ReconcileMs:0.###}ms reset={timing.ResetMs:0.###}ms layout={timing.LayoutMs:0.###}ms paint={timing.PaintMs:0.###}ms"));
            text.Append("Caller-thread allocation: ").Append(timing.AllocatedBytes).Append(" bytes; GC collections: ")
                .Append(timing.Gen0).Append('/').Append(timing.Gen1).Append('/').Append(timing.Gen2)
                .Append("; process GC pause delta: ").Append(timing.GcPauseMs.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine("ms");
        }
        else text.AppendLine("Waiting for a measured host update (subscription active only while inspector is open).");
        text.AppendLine("Inspector queries/UI work are outside host timing spans. GC deltas are process-wide/inclusive.");
        return text.ToString();
    }

    private static void AppendPointerState(StringBuilder text, string label, DocumentInputInspection state)
    {
        text.Append(label).Append(" pointer: hover=").Append(DocumentInspection.Label(state.Hovered))
            .Append(" pressed=").Append(DocumentInspection.Label(state.Pressed))
            .Append(" down=").Append(state.PointerDown)
            .Append(" capture=").Append(state.Captured is DomElement element ? DocumentInspection.Label(element) : state.Captured == null ? "<none>" : "<document>")
            .Append(" pending=").AppendLine(DocumentInspection.Label(state.PendingCapture));
    }

    private static void SetText(TextEdit control, string text)
    {
        if (control.Text == text) return;
        double line = control.ScrollVertical;
        control.Text = text;
        control.ScrollVertical = line;
    }

    private void DrawHighlight()
    {
        if (_host == null) return;
        DomElement? element = _picking ? _hovered : SelectedElement;
        if (element == null) return;
        Transform2D transform = _shield.GetGlobalTransformWithCanvas().AffineInverse() * _host.GetGlobalTransformWithCanvas();
        foreach (var box in _host.DisplayList.Boxes)
        {
            if (!ReferenceEquals(box.Element, element)) continue;
            PaintRect bounds = box.VisualBounds;
            var points = new[] {
                transform * new Vector2((float)bounds.X, (float)bounds.Y),
                transform * new Vector2((float)(bounds.X + bounds.Width), (float)bounds.Y),
                transform * new Vector2((float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height)),
                transform * new Vector2((float)bounds.X, (float)(bounds.Y + bounds.Height)),
                transform * new Vector2((float)bounds.X, (float)bounds.Y) };
            _shield.DrawPolyline(Packed.Vector2s(points), new Color(0.25f, 0.9f, 1f), 2, true);
        }
    }
}
