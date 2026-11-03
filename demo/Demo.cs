using System;
using System.Linq;
using Godot;
using NecronomiDom.Bindings;
using NecronomiDom.Dom;
using NecronomiDom.Paint;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>Wires DOM events of demo/grimoire.html to C#. Run with <c>-- --smoke</c> to lay out, click and quit.</summary>
public partial class Demo : Godot.Node
{
    private DocumentHost _host = null!;
    private readonly GrimoireViewModel _model = new();
    private bool _smoke;
    private int _frames;
    private bool _developerUi;
    private DocumentBindings? _bindings;
    private Element? _summonButton;
    private Element? _banishButton;
    private DelegateEventListener? _summonClick;
    private DelegateEventListener? _banishClick;

    public override void _Ready()
    {
        _host = GetNode<DocumentHost>("UI/Document");
        _smoke = Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0;
        _developerUi = Array.IndexOf(OS.GetCmdlineUserArgs(), "--dev-ui") >= 0;
        BindDocument();
        _host.CancelRequested += OnCancelRequested;
        if (_developerUi)
        {
            _host.DeveloperReloadEnabled = true;
            _host.ReloadCompleted += OnReloadCompleted;
            GD.Print("UI authoring: F6 inspector, F7 reload changed files, F8 force reload.");
        }
    }

    private void BindDocument()
    {
        if (_bindings is { IsDisposed: false }) return;
        var bindings = _host.CreateBindings();
        _bindings = bindings;
        bindings.Disposed += DetachDocument;
        try
        {
            bindings.Text("#count", _model, nameof(GrimoireViewModel.CountText), model => model.CountText);
            _summonButton = _host.Document.GetElementById("summon")!;
            _banishButton = _host.Document.GetElementById("banish")!;
            _summonClick = new DelegateEventListener(_ => Summon());
            _banishClick = new DelegateEventListener(_ => Banish());
            _summonButton.AddEventListener("click", _summonClick);
            _banishButton.AddEventListener("click", _banishClick);
        }
        catch
        {
            bindings.Dispose();
            throw;
        }
    }

    private void DetachDocument()
    {
        _summonButton?.RemoveEventListener("click", _summonClick);
        _banishButton?.RemoveEventListener("click", _banishClick);
        _summonButton = null;
        _banishButton = null;
        _summonClick = null;
        _banishClick = null;
        if (_bindings != null) _bindings.Disposed -= DetachDocument;
        _bindings = null;
    }

    private void OnReloadCompleted(DeveloperReloadResult result) => BindDocument();
    private static void OnCancelRequested() => GD.Print("cancel requested");

    public override void _ExitTree()
    {
        _host.ReloadCompleted -= OnReloadCompleted;
        _host.CancelRequested -= OnCancelRequested;
        _bindings?.Dispose();
    }

    public override void _Input(Godot.InputEvent input)
    {
        if (!_developerUi || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.F6: _host.ToggleInspector(); break;
            case Key.F7: _host.ReloadChangedFiles(); break;
            case Key.F8: _host.ForceReload(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void Summon() => _model.Summon();
    private void Banish() => _model.Banish();

    public override void _Process(double delta)
    {
        if (!_smoke || ++_frames < 3) return;
        _host.FlushPendingLayout();
        var summon = (Element)_host.Document.GetElementById("summon")!;
        ((HTMLElement)summon).Click();
        _host.FlushPendingLayout();
        string count = _host.Document.GetElementById("count")!.TextContent ?? "";
        GD.Print($"smoke: layout={_host.LayoutRevision} boxes={_host.PaintedBoxCount} text={_host.PaintedTextRunCount} " +
            $"backend={_host.TextBackend} count=\"{count}\"");
        bool painted = _host.DisplayList.Runs.Select(run => run is TextEffectsRun effects ? effects.Text : run)
            .OfType<TextRun>().Any(run => run.Owner?.Id == "count" && run.Text == _model.CountText);
        GetTree().Quit(_model.Count == 1 && painted ? 0 : 1);
    }
}
