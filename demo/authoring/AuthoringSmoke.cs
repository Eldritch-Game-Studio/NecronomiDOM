using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NecronomiDom.Dom;
using NecronomiDom.Paint;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>Isolated real-engine exercise of the developer tools; never modifies project UI files.</summary>
public partial class AuthoringSmoke : Godot.Node
{
    private DocumentHost _host = null!;
    private string _directory = "";
    private string _path = "";
    private int _clicks;
    private int _rebinds;
    private int _failures;
    private Document? _bound;

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
        finally
        {
            if (_host != null) { _host.CloseInspector(); _host.QueueFree(); await Frames(3); }
            if (_directory.Length > 0 && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            GD.Print("AUTHORING_SMOKE_RESULT " + (_failures == 0 ? "PASS" : "FAIL"));
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool condition, string name)
    {
        if (!condition) _failures++;
        GD.Print("AUTHORING_SMOKE " + (condition ? "PASS " : "FAIL ") + name);
    }

    private void Bind()
    {
        if (ReferenceEquals(_bound, _host.Document)) return;
        _bound = _host.Document;
        _rebinds++;
        ((HTMLElement)_bound.GetElementById("action")!).AddEventListener("click", new DelegateEventListener(_ => _clicks++));
    }

    private string Markup(string label) => "<!doctype html><html><head><link rel=\"stylesheet\" href=\"screen.css\"></head><body>" +
        "<button id=\"action\">" + label + "</button><input id=\"name\"><div id=\"scroll\"><div id=\"long\">Scrollable content</div></div></body></html>";

    private string Styles(string color) => "html, body, div, button, input { display: block; appearance: none; margin: 0; box-sizing: border-box; } " +
        "body { background-color: #18202a; color: white; font-size: 20px; } " +
        ":root { --accent: " + color + "; } #action { position: absolute; left: 40px; top: 40px; width: 240px; height: 60px; color: var(--accent); } " +
        "#name { position: absolute; left: 40px; top: 120px; width: 240px; height: 40px; } " +
        "#scroll { position: absolute; left: 40px; top: 200px; width: 240px; height: 100px; overflow: auto; } " +
        "#long { height: 600px; padding: 8px; }";

    private async Task Click(float x, float y)
    {
        var position = new Vector2(x, y);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position });
        Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false });
        await Frames();
    }

    private async Task Capture(string name)
    {
        string output = "";
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--proof-out=", StringComparison.Ordinal)) output = arg[12..];
        if (output.Length == 0 || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(output);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(output, name + ".png")) == Error.Ok, name + " screenshot");
    }

    private async Task Run()
    {
        _path = "user://ui/authoring-proof-" + Guid.NewGuid().ToString("N");
        _directory = ProjectSettings.GlobalizePath(_path);
        Directory.CreateDirectory(_directory);
        string html = System.IO.Path.Combine(_directory, "screen.html");
        string css = System.IO.Path.Combine(_directory, "screen.css");
        File.WriteAllText(html, Markup("Inspect this button"));
        File.WriteAllText(css, Styles("#ffd35a"));
        var layer = new CanvasLayer(); AddChild(layer);
        _host = new DocumentHost { DocumentPath = _path + "/screen.html", DeveloperReloadEnabled = true };
        layer.AddChild(_host);
        _host.ReloadCompleted += _ => Bind();
        Bind();
        await Frames();
        _host.FlushPendingLayout();
        var document = _host.Document;
        var input = (HTMLInputElement)document.GetElementById("name")!;
        input.Value = "Keep this edit";
        input.Focus();
        var scroll = document.GetElementById("scroll")!;
        scroll.ScrollTop = 80;
        await Frames();
        double originalScroll = scroll.ScrollTop;
        Check(originalScroll > 0, "real scroll state established");
        File.WriteAllText(css, Styles("#5aff88"));
        Check(_host.ReloadChangedFiles().Succeeded, "changed stylesheet reload succeeds");
        await Frames();
        Check(ReferenceEquals(document, _host.Document), "CSS reload preserves DOM identity");
        var buttonInk = _host.DisplayList.Runs.Select(run => run is TextEffectsRun effects ? effects.Text : run)
            .OfType<TextRun>().First(run => run.Owner?.Id == "action");
        Check(buttonInk.Color.G > .8f && buttonInk.Color.R < .5f, "changed CSS reaches actual painted text");
        Check(input.Value == "Keep this edit" && ReferenceEquals(document.FocusedElement, input), "CSS reload preserves input value and focus");
        Check(scroll.ScrollTop == originalScroll, "CSS reload preserves scroll");
        ((HTMLElement)document.GetElementById("action")!).Click();
        Check(_clicks == 1 && _rebinds == 1, "CSS reload preserves listeners without duplicate rebind");
        var media = new NecronomiDom.Css.Media.MediaEnvironment();
        media.Features["-necronomidom-input"] = "gamepad";
        _host.Styles.Media = media;
        File.WriteAllText(css, Styles("#5aff88") + "@media (-necronomidom-input: gamepad) { #action { display: grid; } }");
        Check(!_host.ReloadChangedFiles().Succeeded, "invalid stylesheet uses current media environment and fails before publication");
        await Frames();
        Check(ReferenceEquals(document, _host.Document) && input.Value == "Keep this edit", "invalid stylesheet leaves current document intact");
        File.WriteAllText(css, Styles("#5aff88"));
        _host.ReloadChangedFiles();
        await Frames();
        _host.OpenInspector();
        await Frames();
        Check(_host.InspectorOpen && !_host.InputEnabled, "inspector opens and suspends document input");
        _host.BeginInspectorPick();
        await Frames();
        int before = _clicks;
        await Click(100, 65);
        Check(_host.InspectorSelection.Contains("action", StringComparison.Ordinal), "actual pointer pick selects rendered button");
        Check(_clicks == before, "inspector pick does not activate underlying button");
        string report = _host.GetInspectorReport();
        GD.Print("AUTHORING_INSPECTOR_REPORT\n" + report);
        Check(report.Contains("screen.css", StringComparison.Ordinal) && report.Contains("--accent", StringComparison.Ordinal), "inspector exposes stylesheet and custom property provenance");
        var selected = _host.AttachInspector().SelectedElement;
        File.WriteAllText(css, Styles("#5aff88") + "#action { border: 1px solid #aaffaa; }");
        Check(_host.ReloadChangedFiles().Succeeded, "CSS reload succeeds with inspector open");
        await Frames();
        Check(ReferenceEquals(selected, _host.AttachInspector().SelectedElement), "CSS reload retains inspector selection");
        await Capture("01-live-inspector");
        var tabs = _host.AttachInspector().FindChildren("*", "TabContainer", true, false).OfType<TabContainer>().Single();
        tabs.CurrentTab = 1;
        await Frames();
        await Capture("01b-matched-rules");
        tabs.CurrentTab = 2;
        await Frames();
        await Capture("01c-computed-values");
        tabs.CurrentTab = 3;
        await Frames();
        await Capture("01d-custom-properties");
        _host.CloseInspector();
        await Frames();
        Check(_host.InputEnabled && !_host.InspectorOpen, "inspector closes and restores input");
        await Click(100, 65);
        Check(_clicks == before + 1, "normal pointer interaction resumes after inspector close");
        File.WriteAllText(html, Markup("Rebound after markup reload"));
        _host.ReloadChangedFiles();
        await Frames();
        Check(!ReferenceEquals(document, _host.Document) && _rebinds == 2, "markup reload replaces document and rebinds once");
        ((HTMLElement)_host.Document.GetElementById("action")!).Click();
        Check(_clicks == before + 2, "replacement document callback works");
        _host.ForceReload();
        await Frames();
        Check(_rebinds == 3, "force reload explicitly rebinds replacement document");
        await Capture("02-reloaded-document");
    }
}
