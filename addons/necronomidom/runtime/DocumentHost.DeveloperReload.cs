#nullable enable
using System;
using Godot;
using NecronomiDom.Html;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Layout;

namespace NecronomiDom.Runtime;

public enum DeveloperReloadKind { Disabled, Unchanged, Styles, Document, Failed }

/// <summary>A completed reload attempt. Rebind element handles/listeners only when DocumentReplaced is true.</summary>
public readonly record struct DeveloperReloadResult(DeveloperReloadKind Kind, string Error = "")
{
    public bool DocumentReplaced => Kind == DeveloperReloadKind.Document;
    public bool Succeeded => Kind is DeveloperReloadKind.Unchanged or DeveloperReloadKind.Styles or DeveloperReloadKind.Document;
}

public partial class DocumentHost
{
    private DocumentFileReload? _reloadFiles;
    private bool _developerReloadEnabled;
    private double _developerReloadElapsed;
    private bool _developerReloadRunning;
    private string _lastAutomaticReloadError = "";
    private const double DeveloperReloadPollSeconds = 0.5;

    /// <summary>Opt-in bounded source polling, only in a debug engine/template. Disabled hosts retain no source snapshots.
    /// First enable captures current disk contents without changing the DOM; use ForceReload for edits predating enable.</summary>
    [Export, BindProperty(Name = "developer_reload_enabled")]
    public bool DeveloperReloadEnabled
    {
        get => _developerReloadEnabled;
        set
        {
            if (_developerReloadEnabled == value) return;
            _developerReloadEnabled = value;
            _reloadFiles = null;
            StopDeveloperReloadWatch();
            if (value && OS.IsDebugBuild() && Document != null) CaptureDeveloperReloadBaseline();
        }
    }

    /// <summary>Whether enabled developer reload also polls source files. Turn off for manual-only reload;
    /// the source baseline is retained. Enabling this alone does not enable developer reload.</summary>
    [Export, BindProperty(Name = "developer_reload_watch_enabled")]
    public bool DeveloperReloadWatchEnabled { get; set; } = true;

    /// <summary>Completion after the new document is mounted. CSS results do not invalidate DOM handles.
    /// Document results require callers to discard old element handles and bind to Document again.
    /// DocumentWithdrawing/DocumentLive remain the authoritative detach/attach publication for shared observers.</summary>
    public event Action<DeveloperReloadResult>? ReloadCompleted;

#if NECRONOMIDOM_GDEXTENSION
    [Signal(Name = "developer_reloaded")]
#else
    [Signal]
#endif
    public delegate void DeveloperReloadedEventHandler(int kind, bool documentReplaced, string error);

    /// <summary>Check actual document/linked-CSS contents, including same-timestamp saves. Runs on the owning UI thread.
    /// Changes require a mounted, nonzero viewport for detached layout/native-paint preflight.
    /// Profile/resource errors leave the live DOM, styles, geometry, input and paint untouched.</summary>
    public DeveloperReloadResult ReloadChangedFiles() => ReloadDeveloperFiles(false);

    /// <summary>Reparse and remount markup even when source bytes are unchanged. Requires a mounted, nonzero viewport
    /// for renderer preflight, and rebinding element handles after success.</summary>
    public DeveloperReloadResult ForceReload() => ReloadDeveloperFiles(true);

    /// <summary>Variant-safe GDScript entry point. Returns the numeric DeveloperReloadKind; details arrive in developer_reloaded.</summary>
    [BindMethod(Name = "reload_changed_files")]
    public int ScriptReloadChangedFiles() => (int)ReloadChangedFiles().Kind;

    [BindMethod(Name = "force_reload")]
    public int ScriptForceReload() => (int)ForceReload().Kind;

    private DeveloperReloadResult ReloadDeveloperFiles(bool force, bool automatic = false)
    {
        if (!DeveloperReloadEnabled || !OS.IsDebugBuild()) return CompleteDeveloperReload(new(DeveloperReloadKind.Disabled), automatic);
        if (_developerReloadRunning) return new(DeveloperReloadKind.Unchanged);
        if (Document == null || DocumentPath.Length == 0) return CompleteDeveloperReload(new(DeveloperReloadKind.Failed, "No file document is loaded."), automatic);
        DeveloperReloadResult result;
        _developerReloadRunning = true;
        try
        {
            if (force && _reloadFiles == null)
            {
                var replacement = DocumentFileReload.Prepare(DocumentPath, ReadDeveloperSource);
                ValidateDeveloperDocument(replacement.CreateValidationDocument());
                LoadPreparedDocument(new PreparedDocument(replacement.Path, replacement.Content, replacement));
                result = new(DeveloperReloadKind.Document);
            }
            else
            {
                _reloadFiles ??= DocumentFileReload.Attach(Document, DocumentPath, ReadDeveloperSource);
                var update = _reloadFiles.PrepareUpdate(ReadDeveloperSource, force);
                if (update.Replacement is { } replacement)
                {
                    ValidateDeveloperDocument(replacement.CreateValidationDocument());
                    LoadPreparedDocument(new PreparedDocument(replacement.Path, replacement.Content, replacement));
                    result = new(DeveloperReloadKind.Document);
                }
                else if (update.StylesChanged)
                {
                    update.ApplyStyles(ValidateDeveloperDocument);
                    MarkDirty();
                    foreach (var diagnostic in update.Diagnostics) GD.PushWarning(diagnostic.ToString());
                    result = new(DeveloperReloadKind.Styles);
                }
                else result = new(DeveloperReloadKind.Unchanged);
            }
        }
        catch (Exception error)
        {
            result = new(DeveloperReloadKind.Failed, error.Message);
        }
        finally { _developerReloadRunning = false; }
        return CompleteDeveloperReload(result, automatic);
    }

    private DeveloperReloadResult CompleteDeveloperReload(DeveloperReloadResult result, bool automatic)
    {
        // Idle checks are silent; unchanged invalid edits produce one actionable diagnostic, not two per second.
        if (automatic && (result.Kind == DeveloperReloadKind.Unchanged || result.Kind == DeveloperReloadKind.Disabled ||
            result.Kind == DeveloperReloadKind.Failed && result.Error == _lastAutomaticReloadError)) return result;
        _lastAutomaticReloadError = result.Kind == DeveloperReloadKind.Failed ? result.Error : "";
        if (result.Kind == DeveloperReloadKind.Failed) GD.PushError("NecronomiDOM reload: " + result.Error);
        ReloadCompleted?.Invoke(result);
#if NECRONOMIDOM_GDEXTENSION
        EmitSignalDeveloperReloaded((int)result.Kind, result.DocumentReplaced, result.Error);
#else
        EmitSignal(SignalName.DeveloperReloaded, (int)result.Kind, result.DocumentReplaced, result.Error);
#endif
        return result;
    }

    private void ValidateDeveloperDocument(Document preview)
    {
        if (!_entryActive || Size.X <= 0 || Size.Y <= 0)
            throw new InvalidOperationException("Developer reload validation requires a mounted host with a nonzero viewport.");
        RuntimeStyleProfile.Validate(preview);
        var styles = new StyleEngine(preview) { IncrementalReconciliation = RetainedUpdates };
        var liveMedia = Styles.Media;
        var media = new NecronomiDom.Css.Media.MediaEnvironment
        {
            Type = liveMedia.Type, Width = liveMedia.Width, Height = liveMedia.Height,
            Resolution = liveMedia.Resolution, RootFontSize = liveMedia.RootFontSize,
            UnitScale = liveMedia.UnitScale, ContainerStyle = liveMedia.ContainerStyle
        };
        media.Features.Clear();
        foreach (var feature in liveMedia.Features) media.Features[feature.Key] = feature.Value;
        foreach (var custom in liveMedia.CustomMedia) media.CustomMedia[custom.Key] = custom.Value;
        styles.Media = media;
        styles.UpdateViewport(Size.X, Size.Y);
        using var fonts = new NativeFontFamilies(FontFamily, FontPath, FontPaths, FallbackFontPaths);
        using var images = new NativeImageSource { LinearComposition = LinearComposition };
        fonts.Refresh(preview);
        fonts.BeginLayoutPass();
        var opacity = new OpacityTransitionController();
        var background = new BackgroundColorTransitionController(_backgroundColorController.Profile);
        var width = new WidthTransitionController(_widthController.Profile);
        var scale = new ScaleTransitionController(_scaleController.Profile);
        var foreground = new ForegroundColorTransitionController(_foregroundColorController.Profile);
        ObserveTransitions(preview);
        foreground.ReconcileStyles(preview, styles, 0);
        var layout = new FlowLayout(styles, fonts.Metrics, images, opacity, background, width, scale, foreground);
        var display = layout.Layout(preview, Size.X, Size.Y);
        // Exercise the same native paint/resource constraints, on a hidden temporary subtree, before publication.
        var paint = new DocumentPaint { Visible = false, LinearComposition = LinearComposition };
        try
        {
            AddChild(paint);
            paint.Rebuild(display, fonts, images);
        }
        finally
        {
            paint.WithdrawDocument();
            if (paint.GetParent() != null) RemoveChild(paint);
            paint.Free();
        }

        void ObserveTransitions(NecronomiDom.Dom.Node node)
        {
            if (node is Element element)
            {
                var style = styles.GetComputedStyle(element);
                if (style.GetPropertyValue("display") == "none") return;
                var opacityStyle = OpacityTransitionStyle.Resolve(style);
                if (opacityStyle.Enabled && (opacityStyle.Duration > 0 || opacityStyle.Delay > 0)) opacity.UpdateStyle(element, style, 0);
                var backgroundStyle = BackgroundColorTransitionStyle.Resolve(style);
                if (backgroundStyle.Enabled && (backgroundStyle.Duration > 0 || backgroundStyle.Delay > 0)) background.UpdateStyle(element, style, 0);
                var values = styles.GetComputedValues(element);
                var widthStyle = WidthTransitionStyle.Resolve(values);
                if (widthStyle.Enabled && (widthStyle.Duration > 0 || widthStyle.Delay > 0)) width.UpdateStyle(element, values, 0);
                var scaleStyle = ScaleTransitionStyle.Resolve(values);
                if (scaleStyle.Enabled && (scaleStyle.Duration > 0 || scaleStyle.Delay > 0)) scale.UpdateStyle(element, values, 0);
            }
            for (var child = node.FirstChild; child != null; child = child.NextSibling) ObserveTransitions(child);
        }
    }

    private void CaptureDeveloperReloadBaseline()
    {
        try { _reloadFiles = DocumentFileReload.Attach(Document, DocumentPath, ReadDeveloperSource); }
        catch (Exception error) { CompleteDeveloperReload(new(DeveloperReloadKind.Failed, error.Message), false); }
    }

    private void ProcessDeveloperReload(double delta)
    {
        if (!DeveloperReloadEnabled || !DeveloperReloadWatchEnabled || !OS.IsDebugBuild() || Document == null) return;
        _developerReloadElapsed += delta;
        if (_developerReloadElapsed < DeveloperReloadPollSeconds) return;
        _developerReloadElapsed = 0;
        ReloadDeveloperFiles(false, true);
    }

    // No background watcher, thread, timer, directory recursion or queued callback survives exit/disable.
    private void StopDeveloperReloadWatch()
    {
        _developerReloadElapsed = 0;
        _lastAutomaticReloadError = "";
    }
}
