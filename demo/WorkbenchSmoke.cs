using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDomDemo;

/// <summary>Regression entrypoint for the actual default demo. Reload edits are isolated copies, never repo files.</summary>
public partial class Demo
{
    private string _proofDirectory = "";
    private int _smokeFailures;
    private void PrepareProofSources()
    {
        string path = "user://ui/workbench-proof-" + Guid.NewGuid().ToString("N");
        _proofDirectory = ProjectSettings.GlobalizePath(path);
        Directory.CreateDirectory(_proofDirectory);
        foreach (string file in new[] { "workbench.html", "workbench.css" })
            File.Copy(ProjectSettings.GlobalizePath("res://demo/" + file), Path.Combine(_proofDirectory, file));
        _host.LoadDocument(path + "/workbench.html");
    }
    private void CleanProofSources()
    {
        if (_proofDirectory.Length > 0 && Directory.Exists(_proofDirectory)) Directory.Delete(_proofDirectory, true);
    }
    private async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _host.FlushPendingLayout();
    }
    private void Check(bool condition, string label)
    {
        if (!condition) _smokeFailures++;
        GD.Print("WORKBENCH_SMOKE " + (condition ? "PASS " : "FAIL ") + label);
    }
    private bool Painted(string id, string? text = null) => _host.DisplayList.Runs
        .Select(run => run is TextEffectsRun effects ? effects.Text : run).OfType<TextRun>()
        .Any(run => run.Owner?.Id == id && (text == null || run.Text.Contains(text, StringComparison.Ordinal)));
    private async Task Click(string id)
    {
        var bounds = _host.DisplayList.Boxes.First(box => box.Element.Id == id).Bounds;
        var position = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        // Input.ParseInputEvent enters at window coordinates, before Godot's logical viewport transform.
        position = GetViewport().GetFinalTransform() * position;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position });
        Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false });
        await Frames();
    }
    private async Task Press(Key key, bool control = false, uint unicode = 0)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = control, Unicode = unicode, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = control, Pressed = false });
        await Frames(1);
    }
    private async Task Type(string id, string text)
    {
        await Click(id);
        await Press(Key.A, control: true);
        await Press(Key.Backspace);
        foreach (char character in text) await Press(Key.None, unicode: character);
        await Frames();
    }
    private async Task Capture(string name)
    {
        string output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--proof-out=", StringComparison.Ordinal))?[12..] ?? "";
        if (output.Length == 0 || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(output);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, name + " screenshot");
    }
    private async void RunSmoke(bool full)
    {
        try
        {
            // Godot's dummy headless window is 64x64 regardless of --resolution. Give the smoke a
            // logical viewport; rendered runs continue using their real, unmodified native window.
            if (DisplayServer.GetName() == "headless")
            {
                GetTree().Root.ContentScaleSize = new Vector2I(1440, 1000);
                GetTree().Root.ContentScaleMode = Godot.Window.ContentScaleModeEnum.Viewport;
            }
            await Frames(5);
            GD.Print($"WORKBENCH_SMOKE viewport={GetViewport().GetVisibleRect().Size} host={_host.Size} boxes={_host.DisplayList.Boxes.Count} runs={_host.DisplayList.Runs.Count}");
            Check(Painted("character-preview", "Aster Vale") && !Painted("tracker-title"), "initial bound text and hidden tracker reach paint");
            Check(Element("save-profile").HasAttribute("disabled"), "unchanged profile starts disabled");
            await Capture("00-workbench");
            await Click("accept-0");
            Check(_model.HasQuest && Painted("tracker-title", "Lights in the marsh"), "real quest acceptance reveals bound tracker");
            Check(Element("accept-1").HasAttribute("disabled"), "one active quest disables other accept buttons");
            await Click("accept-1");
            Check(_model.TrackerTitle == "Lights in the marsh", "disabled action rejects real pointer activation");
            if (full) await RunAuthoringSmoke();
            GD.Print($"smoke: layout={_host.LayoutRevision} boxes={_host.PaintedBoxCount} text={_host.PaintedTextRunCount} backend={_host.TextBackend}");
        }
        catch (Exception error) { _smokeFailures++; GD.PushError(error.ToString()); }
        finally
        {
            _host.CloseInspector();
            GD.Print("WORKBENCH_SMOKE_RESULT " + (_smokeFailures == 0 ? "PASS" : "FAIL"));
            GetTree().Quit(_smokeFailures == 0 ? 0 : 1);
        }
    }
    private async Task RunAuthoringSmoke()
    {
        await Click("reload");
        Check(_model.ReloadStatus.StartsWith("No source changes", StringComparison.Ordinal), "manual toolbar reports unchanged sources distinctly");
        await Type("name", "");
        Check(! _model.CanSave && Element("save-profile").HasAttribute("disabled") && Painted("character-preview", "unnamed"), "blank name disables saving while preview stays live");
        await Click("save-profile");
        Check(_model.SavedName == "Aster Vale", "disabled save rejects real pointer activation");
        await Type("name", "Rowan Ash");
        await Type("motto", "Keep the beacons burning.");
        Check(_model.DraftName == "Rowan Ash" && _model.DraftMotto == "Keep the beacons burning.", "real keyboard input reaches explicit C# input handlers");
        Check(Painted("character-preview", "Rowan Ash") && Element("character-card").ClassList.Contains("editing") && !Element("save-profile").HasAttribute("disabled"), "typed text, class and enabled bindings update together");
        await Click("equip");
        Check(Element("loadout").ClassList.Contains("ready") && Painted("lantern-hint"), "state class and conditional visibility reach paint");
        await Click("equip");
        Check(!Element("loadout").ClassList.Contains("ready") && !Painted("lantern-hint"), "stowing removes the state class and hides the painted hint");
        await Click("equip");
        await Click("record");
        Check(Painted("tracker-progress", "1 / 3"), "tracker follows model progress");
        await Capture("01-bound-state");
        await Click("name");
        var input = (HTMLInputElement)Element("name");
        input.SetSelectionRange(1, 4, "forward");
        var document = _host.Document;
        var scope = _bindings;
        string cssPath = Path.Combine(_proofDirectory, "workbench.css");
        string css = File.ReadAllText(cssPath);
        File.WriteAllText(cssPath, css.Replace("--accent: #b9e38c;", "--accent: #eebd78;", StringComparison.Ordinal));
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        Check(!_host.DeveloperReloadWatchEnabled && !_model.ReloadStatus.StartsWith("CSS applied", StringComparison.Ordinal), "no automatic reload without source-watch opt-in");
        await Press(Key.F7);
        await Frames();
        Check(_model.ReloadStatus.StartsWith("CSS applied", StringComparison.Ordinal) && ReferenceEquals(document, _host.Document) && ReferenceEquals(scope, _bindings), "F7 applies linked CSS without replacing document or binding scope");
        Check(input.Value == "Rowan Ash" && ReferenceEquals(document.FocusedElement, input) && input.SelectionStart == 1 && input.SelectionEnd == 4, "CSS reload retains actual form value, selection and focus");
        var accentInk = _host.DisplayList.Runs.Select(run => run is TextEffectsRun effects ? effects.Text : run).OfType<TextRun>().First(run => run.Owner?.Id == "tracker-progress");
        Check(accentInk.Color.R > .9f && accentInk.Color.G > .6f && accentInk.Color.B < .6f, "edited accent reaches native painted text");
        await Capture("02-css-focus-retained");
        string htmlPath = Path.Combine(_proofDirectory, "workbench.html");
        string html = File.ReadAllText(htmlPath);
        File.WriteAllText(htmlPath, html.Replace("Keep the story. Edit the UI.", "Keep the state. Edit the UI.", StringComparison.Ordinal));
        await Press(Key.F8);
        await Frames();
        Check(!ReferenceEquals(document, _host.Document) && scope!.IsDisposed && _model.ReloadStatus.StartsWith("Markup replaced", StringComparison.Ordinal), "F8 replaces authored markup and disposes old bindings");
        Check(((HTMLInputElement)Element("name")).Value == "Rowan Ash" && ((HTMLInputElement)Element("motto")).Value == "Keep the beacons burning." && _model.LanternEquipped && Painted("tracker-progress", "1 / 3"), "markup replacement retains model and restores form drafts");
        await Click("record");
        Check(Painted("tracker-progress", "2 / 3"), "rebound quest handler runs exactly once");
        await Click("save-profile");
        Check(_model.SavedName == "Rowan Ash" && Element("save-profile").HasAttribute("disabled") && !Element("character-card").ClassList.Contains("editing"), "rebound save handler updates enabled and class bindings");
        await Type("name", "Rowan Night");
        Check(_model.DraftName == "Rowan Night" && Painted("character-preview", "Rowan Night"), "rebound input handler continues editing after markup reload");
        await Capture("03-markup-rebound");
        document = _host.Document;
        File.AppendAllText(cssPath, "\nbody { display: grid; }\n");
        await Press(Key.F7);
        await Frames();
        Check(_model.ReloadFailed && _model.ReloadStatus.Contains("grid", StringComparison.OrdinalIgnoreCase) && Painted("reload-status"), "rejected CSS displays actionable runtime error");
        Check(ReferenceEquals(document, _host.Document) && ((HTMLInputElement)Element("name")).Value == "Rowan Night", "rejected reload retains working document and input");
        await Click("record");
        Check(_model.CanClaim && Element("tracker").ClassList.Contains("complete") && !Element("claim").HasAttribute("disabled"), "working controls and bound completion state survive rejection");
        await Click("record");
        Check(_model.TrackerProgress.StartsWith("3 / 3", StringComparison.Ordinal), "disabled progress button rejects extra input");
        await Capture("04-reload-rejected");
        File.WriteAllText(cssPath, css);
        await Click("reload");
        Check(!_model.ReloadFailed && _model.ReloadStatus.StartsWith("CSS applied", StringComparison.Ordinal), "manual toolbar recovers after fixing rejected source");
        await Click("claim");
        Check(_model.Supplies == 12 && !Painted("tracker-title") && Painted("supply-count", "12 supplies"), "claim updates rewards and hides tracker");
        await Click("accept-0");
        Check(!_model.HasQuest && _model.Supplies == 12, "completed quest cannot be accepted or rewarded twice");
        await Click("accept-1");
        await Click("force-reload");
        await Click("record");
        Check(_model.TrackerTitle == "The missing pages" && _model.TrackerProgress.StartsWith("1 / 3", StringComparison.Ordinal), "unchanged force reload also rebinds handlers once");
        await Click("watch");
        Check(_host.DeveloperReloadWatchEnabled && _model.Watching, "visible source-watch action explicitly enables polling");
        File.WriteAllText(cssPath, css.Replace("--accent: #b9e38c;", "--accent: #eebd78;", StringComparison.Ordinal));
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        await Frames();
        Check(_model.ReloadStatus.StartsWith("CSS applied", StringComparison.Ordinal), "opted-in watcher applies a real source edit");
        await Click("watch");
        Check(!_host.DeveloperReloadWatchEnabled && !_model.Watching, "visible action stops automatic source polling");
        await Click("reference");
        Check(!Element("reference-panel").HasAttribute("hidden"), "capability reference and editor configuration are discoverable");
        await Capture("05-editor-reference");
        await Click("reference");
        await Press(Key.F6);
        await Press(Key.F6);
        Check(!_host.InspectorOpen, "F6 closes the developer inspector");
        await Click("inspect");
        _host.SelectInspectorElement("#tracker");
        var inspector = _host.AttachInspector();
        foreach (var control in inspector.FindChildren("*", nameof(TabContainer), recursive: true, owned: false))
        {
            if (control is not TabContainer tabs) continue;
            for (int index = 0; index < tabs.GetTabCount(); index++)
                if (tabs.GetTabTitle(index) == "Variables") { tabs.CurrentTab = index; break; }
        }
        await Frames();
        Check(_host.InspectorOpen && ReferenceEquals(inspector.SelectedElement, Element("tracker")), "visible toolbar inspects the actual bound tracker");
        await Capture("06-live-inspector");
    }
}
