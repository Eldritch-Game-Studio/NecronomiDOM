using Godot;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>
/// Disk HTML/CSS → DOM/cascade → bounded flow → display list → Advanced text and MultiMesh paint.
/// A host is one document under its caller's nonzero CanvasLayer; no Godot Buttons/Labels or focus search.
/// Unsupported profiles are exceptions, not fallback UI. See FlowLayout for the executable property set.
/// </summary>
public partial class DocumentHost : Control
{
    /// <summary>res:// (or user://ui/) HTML document loaded when the host becomes ready.</summary>
    [Export] public string DocumentPath { get; set; } = "";
    /// <summary>Font file for <see cref="FontFamily"/>, the family documents name in <c>font-family</c>.</summary>
    [Export] public string FontPath { get; set; } = "res://addons/necronomidom/fonts/NotoSans-Regular.otf";
    [Export] public string FontFamily { get; set; } = "Noto Sans";
    /// <summary>Additional family name → font file mappings; <c>@font-face</c> rules in document stylesheets also map families.</summary>
    [Export] public Godot.Collections.Dictionary<string, string> FontPaths { get; set; } = new();
    /// <summary>Ordered font files searched for code points a family's own face lacks (for example CJK or emoji fonts).</summary>
    [Export] public string[] FallbackFontPaths { get; set; } = System.Array.Empty<string>();
    public Document Document { get; private set; } = null!;
    public DisplayList DisplayList { get; private set; } = new();
    public StyleEngine Styles { get; private set; } = null!;
    public int LayoutRevision { get; private set; }
    public int PaintRevision => _paint.RebuildRevision;
    public string TextBackend => _shaper?.Backend ?? "";
    /// <summary>Diagnostic full-update reference for paired document-cost measurements.</summary>
    public bool RetainedUpdates { get; init; } = true;
    public int ShapedRunCount => _shaper?.ShapedRunCount ?? 0;
    public int PaintedBoxCount => _paint.BoxInstanceCount;
    public int PaintedTextRunCount => _paint.TextRunCount;
    public int PaintedImageRunCount => _paint.ImageRunCount;
    public int PointerActivationCount => _documentInput?.PointerActivationCount ?? 0;
    private NativeFontFamilies? _shaper;
    private FlowLayout? _layout;
    private DocumentInvalidation? _invalidation;
    private readonly DocumentPaint _paint = new();
    /// <summary>Composite the document in linear light (blending in linear space, as some engine UI systems do);
    /// default CSS documents composite in sRGB.</summary>
    public bool LinearComposition
    {
        get => _paint.LinearComposition;
        init => _paint.LinearComposition = value;
    }
    private NativeImageSource? _images;
    private bool _dirty;
    private DocumentInput? _documentInput;
    private Viewport? _fittedViewport;
    private bool _readyInitialized, _entryActive;
    public override void _EnterTree()
    {
        // Godot calls Ready only once, but this same host may enter many trees.
        if (_readyInitialized) MountResources();
    }
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.All;
        AddChild(_paint);
        _readyInitialized = true;
        MountResources();
        if (Document == null && DocumentPath.Length > 0) LoadDocument(DocumentPath);
    }
    private void FitViewport() => Size = _fittedViewport!.GetVisibleRect().Size / _textScale;
    private void MarkDirty() => _dirty = true;
    private void MountResources()
    {
        if (_entryActive) return;
        _entryActive = true;
        InitializeInput();
        Resized += MarkDirty;
        MountTextScale();
        if (GetParent() is not Control)
        {
            _fittedViewport = GetViewport();
            _fittedViewport.SizeChanged += FitViewport;
            FitViewport();
        }
        if (Document != null) CreateDocumentResources();
    }
    private void CreateDocumentResources()
    {
        _paint.RetainedUpdates = RetainedUpdates;
        _images = new NativeImageSource { LinearComposition = LinearComposition };
        _shaper = new NativeFontFamilies(FontFamily, FontPath, FontPaths, FallbackFontPaths);
        _shaper.Refresh(Document);
        _layout = new FlowLayout(Styles, _shaper.Metrics, _images, _opacityController, _backgroundColorController,
            _widthController, _scaleController, _foregroundColorController);
        _documentInput = new DocumentInput(Document, Styles) { TextHitTest = HitTextControl, ScrollChanged = MarkDirty };
        _invalidation = new DocumentInvalidation(Document, MarkDirty, element => _inlineStyleChanges.Add(element));
        _dirty = true;
        MountDocumentAccessibility();
        PublishLiveDocument();
    }
    private void WithdrawDocumentResources()
    {
        RetractLiveDocument();
        WithdrawDocumentAccessibility();
        // Detached background slots still borrow images; retire them before the image owner.
        _paint.WithdrawDocument();
        ClearTextControls();
        _layout = null;
        var invalidation = _invalidation; _invalidation = null; invalidation?.Dispose();
        var input = _documentInput; _documentInput = null; input?.Dispose();
        var images = _images; _images = null; images?.Dispose();
        var shaper = _shaper; _shaper = null; shaper?.Dispose();
        double now = MonotonicSeconds();
        ClearDocumentOpacity(now);
        ClearDocumentBackgroundColor(now);
        ClearDocumentGeometry(now);
        ClearDocumentForegroundColor(now);
    }
    internal NativeImageOwnership[] CaptureImageOwnership() =>
        _images?.CaptureOwnership() ?? System.Array.Empty<NativeImageOwnership>();
    internal NativeFontOwnership[] CaptureFontOwnership() =>
        _shaper?.CaptureOwnership() ?? System.Array.Empty<NativeFontOwnership>();
    public override void _Process(double delta)
    {
        if (!_entryActive) return;
        bool measureUpdate = UpdateMeasured != null || ObserveEntryUpdates;
        UpdateTimer timer = measureUpdate ? new UpdateTimer() : default;
        ProcessDocumentInput(delta);
        double now = MonotonicSeconds();
        if (_invalidation?.StyleSheetsChanged() == true) _dirty = true;
        bool opacityChanged = _opacityController.Sample(now);
        // Color lives in immutable paint runs; invalidate the existing retained paint path only on an actual sample change.
        if (_backgroundColorController.Sample(now)) _dirty = true;
        if (_foregroundColorController.Sample(now)) _dirty = true;
        if (_widthController.Sample(now) | _scaleController.Sample(now)) _dirty = true;
        if (opacityChanged && OpacityPhaseEnded()) _dirty = true;
        if (_layout != null && Size.X > 0)
        {
            bool layoutChanged = LayoutPendingChanges(now, measureUpdate, ref timer);
            if (!layoutChanged && opacityChanged) UpdateOpacityGroups();
            if (measureUpdate) timer.Mark();
            UpdateTextCaret(layoutChanged);
            if (measureUpdate) timer.PaintMs = timer.Mark();
        }
        // UA handlers may mutate/reload the DOM; flush only after all collection/paint iteration.
        _opacityController.DispatchEvents();
        _backgroundColorController.DispatchEvents();
        _foregroundColorController.DispatchEvents();
        DispatchGeometryEvents();
        Document?.DispatchTextSelectionEvents();
        _documentInput?.DispatchScrollEvents();
        SynchronizeDocumentAccessibility();
        if (measureUpdate) PublishUpdateTiming(timer.Finish());
    }
    /// <summary>Resolve late DOM changes before a caller measures or presents this frame's geometry.
    /// Does not advance clocks, repeat input, or dispatch events; clean documents do no work.</summary>
    public void FlushPendingLayout()
    {
        if (!_entryActive || _layout == null || Size.X <= 0 || (!_dirty && _inlineStyleChanges.Count == 0)) return;
        UpdateTimer timer = default;
        if (LayoutPendingChanges(MonotonicSeconds(), false, ref timer)) UpdateTextCaret(true);
    }
    private bool LayoutPendingChanges(double now, bool measureUpdate, ref UpdateTimer timer)
    {
        if (!_dirty && _inlineStyleChanges.Count == 0) return false;
        if (!_dirty && RetainedUpdates && TryUpdateInlineCompositor())
        { _inlineStyleChanges.Clear(); return false; }
        _inlineStyleChanges.Clear();
        _dirty = false;
        RuntimeStyleProfile.Validate(Document);
        // Layout follows the new dimensions; cached cascade/computed values need not depend on them.
        Styles.UpdateViewport(Size.X, Size.Y);
        ReconcileOpacityStyles(now);
        ReconcileBackgroundColorStyles(now);
        ReconcileGeometryStyles(now);
        ReconcileForegroundColorStyles(now);
        if (measureUpdate) timer.ReconcileMs = timer.Mark();
        if (!RetainedUpdates) _paint.Clear();
        _shaper!.Refresh(Document, _paint.Clear); _shaper.BeginLayoutPass();
        if (measureUpdate) timer.ResetMs = timer.Mark();
        DisplayList = _layout!.Layout(Document, Size.X, Size.Y);
        if (measureUpdate) timer.LayoutMs = timer.Mark();
        CaptureGeometryWidths();
        LayoutRevision++;
        GeometryRevision++;
        BindOpacityGroups();
        _documentInput!.UpdateGeometry(DisplayList, Size.X, Size.Y);
        if (Document.FocusedElement != null && InputEnabled && !HasFocus()) GrabFocus();
        _invalidation!.CaptureStyleSheets();
        return true;
    }
    public override bool _HasPoint(Vector2 point) => InputEnabled && (_documentInput?.HasPoint(point.X, point.Y) ?? false);
    public override int _GetCursorShape(Vector2 atPosition)
    {
        if (!_entryActive || _documentInput == null) return (int)CursorShape.Arrow;
        var target = DisplayList.HitTest(atPosition.X, atPosition.Y);
        string cursor = target != null ? Styles.GetComputedStyle(target).GetPropertyValue("cursor") : "auto";
        return (int)(cursor == "pointer" ? CursorShape.PointingHand
            : cursor == "text" || cursor == "auto" && target is ITextControl ? CursorShape.Ibeam : CursorShape.Arrow);
    }
    public override void _ExitTree()
    {
        if (!_entryActive) return;
        _entryActive = false;
        WithdrawDocumentAccessibility();
        Resized -= MarkDirty;
        WithdrawTextScale();
        if (_fittedViewport != null)
        {
            _fittedViewport.SizeChanged -= FitViewport;
            _fittedViewport = null;
        }
        DisposeInput();
        WithdrawDocumentResources();
        _opacityController.DispatchEvents();
        _backgroundColorController.DispatchEvents();
        DispatchGeometryEvents();
    }
}

