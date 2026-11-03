using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NecronomiDom.Bindings;
using NecronomiDom.Dom;
using NecronomiDom.Paint;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>Exercises one-way bindings through actual input, paint and document replacement.</summary>
public partial class BindingSmoke : Godot.Node
{
    private sealed class State : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _changed;
        public event PropertyChangedEventHandler? PropertyChanged { add => _changed += value; remove => _changed -= value; }
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public int Count { get; private set; }
        public string CountText => Count.ToString(CultureInfo.InvariantCulture);
        public bool Active => Count % 2 == 1;
        public bool Visible => Count > 0;
        public bool Enabled => Count < 2;
        public void Set(int value) { Count = value; _changed?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }

    private readonly State _state = new();
    private DocumentHost _host = null!;
    private DocumentBindings? _bindings;
    private string _directory = "";
    private int _failures;
    private int _bindCount;
    private int _commands;
    private const string Markup = "<!doctype html><html><head><link rel=\"stylesheet\" href=\"screen.css\"></head><body>" +
        "<button id=\"action\">Advance</button><p id=\"status\"></p><p id=\"optional\">Bound optional content</p></body></html>";
    private static string Css(string color) => "html,body,button,p { display:block; margin:0; padding:0; appearance:none; box-sizing:border-box; font-size:20px; } " +
        "body { background-color:#18202a; color:" + color + "; } [hidden] {display:none!important;} " +
        "#action { position:absolute; left:40px; top:40px; width:240px; height:60px; } " +
        "#status {position:absolute; left:40px; top:120px; width:240px;} " +
        "#status.active {color:#5aff88;} #optional {position:absolute; left:40px; top:200px; width:400px;}";

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
        finally
        {
            if (_host != null && GodotObject.IsInstanceValid(_host)) { _host.QueueFree(); await Frames(); }
            if (_directory.Length > 0 && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            GD.Print("BINDING_SMOKE_RESULT " + (_failures == 0 ? "PASS" : "FAIL"));
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool value, string label)
    {
        if (!value) _failures++;
        GD.Print("BINDING_SMOKE " + (value ? "PASS " : "FAIL ") + label);
    }
    private void Bind()
    {
        _bindings = _host.CreateBindings();
        _bindings.Text("#status", _state, nameof(State.CountText), model => model.CountText);
        _bindings.Class("#status", "active", _state, nameof(State.Active), model => model.Active);
        _bindings.Visible("#optional", _state, nameof(State.Visible), model => model.Visible);
        _bindings.Enabled("#action", _state, nameof(State.Enabled), model => model.Enabled);
        ((HTMLElement)_host.Document.GetElementById("action")!).AddEventListener("click", new DelegateEventListener(_ =>
        {
            _commands++;
            _state.Set(_state.Count + 1);
        }));
        _bindCount++;
    }
    private System.Collections.Generic.IEnumerable<TextRun> Ink() => _host.DisplayList.Runs
        .Select(run => run is TextEffectsRun effects ? effects.Text : run).OfType<TextRun>();
    private async Task Click()
    {
        var point = new Vector2(100, 65);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
        Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false });
        await Frames();
    }
    private async Task Capture()
    {
        string output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--proof-out=", StringComparison.Ordinal))?[12..] ?? "";
        if (output.Length == 0 || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(output);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(output, "typed-bindings.png")) == Error.Ok, "binding screenshot");
    }
    private async Task Run()
    {
        string path = "user://ui/binding-proof-" + Guid.NewGuid().ToString("N");
        _directory = ProjectSettings.GlobalizePath(path); Directory.CreateDirectory(_directory);
        string html = System.IO.Path.Combine(_directory, "screen.html");
        string css = System.IO.Path.Combine(_directory, "screen.css");
        File.WriteAllText(html, Markup); File.WriteAllText(css, Css("white"));
        var layer = new CanvasLayer(); AddChild(layer);
        _host = new DocumentHost { DocumentPath = path + "/screen.html", DeveloperReloadEnabled = true };
        layer.AddChild(_host);
        Bind();
        _host.ReloadCompleted += result => { if (result.DocumentReplaced) Bind(); };
        await Frames();
        Check(Ink().Any(run => run.Owner?.Id == "status" && run.Text == "0"), "initial state is painted");
        Check(!Ink().Any(run => run.Owner?.Id == "optional"), "visibility binding removes optional content from paint");
        await Click();
        Check(_commands == 1 && _state.Count == 1, "real click invokes explicit command once");
        Check(Ink().Any(run => run.Owner?.Id == "status" && run.Text == "1" && run.Color.G > .8f && run.Color.R < .5f), "state text and class styling reach native paint");
        Check(Ink().Any(run => run.Owner?.Id == "optional"), "visibility binding restores actual painted content");
        await Capture();
        await Click();
        await Click();
        Check(_commands == 2 && _state.Count == 2, "enabled binding blocks further real button activation");
        _state.Set(0); await Frames();
        var scope = _bindings!;
        File.WriteAllText(css, Css("#ff885a"));
        Check(_host.ReloadChangedFiles().Succeeded, "CSS reload with bindings succeeds");
        await Frames();
        Check(ReferenceEquals(scope, _bindings) && !scope.IsDisposed && _bindCount == 1, "CSS reload preserves the binding scope");
        await Click();
        Check(_commands == 3 && _state.Count == 1, "bindings and command remain live after CSS reload");
        var oldStatus = _host.Document.GetElementById("status")!;
        File.WriteAllText(html, Markup + "<!-- replacement -->");
        Check(_host.ReloadChangedFiles().Succeeded, "markup reload succeeds");
        await Frames();
        Check(scope.IsDisposed && _bindCount == 2 && !ReferenceEquals(scope, _bindings), "markup withdrawal disposes old scope and creates one replacement");
        _state.Set(2); await Frames();
        Check(oldStatus.TextContent == "1" && Ink().Any(run => run.Owner?.Id == "status" && run.Text == "2"), "notifications update only the new document");
        var finalScope = _bindings!;
        _host.QueueFree(); await Frames();
        Check(finalScope.IsDisposed && _state.Subscribers == 0, "host exit unsubscribes the state source");
        _state.Set(3);
        Check(oldStatus.TextContent == "1", "retired document remains isolated after host exit");
    }
}
