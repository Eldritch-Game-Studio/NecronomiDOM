using System;
using Godot;
using NecronomiDom.Dom;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>Wires DOM events of demo/grimoire.html to C#. Run with <c>-- --smoke</c> to lay out, click and quit.</summary>
public partial class Demo : Godot.Node
{
    private DocumentHost _host = null!;
    private int _summoned;
    private bool _smoke;
    private int _frames;
    private bool _developerUi;
    private Document? _boundDocument;

    public override void _Ready()
    {
        _host = GetNode<DocumentHost>("UI/Document");
        _smoke = Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0;
        _developerUi = Array.IndexOf(OS.GetCmdlineUserArgs(), "--dev-ui") >= 0;
        BindDocument();
        _host.CancelRequested += () => GD.Print("cancel requested");
        if (_developerUi)
        {
            _host.DeveloperReloadEnabled = true;
            _host.ReloadCompleted += _ => BindDocument();
            GD.Print("UI authoring: F6 inspector, F7 reload changed files, F8 force reload.");
        }
    }

    private void BindDocument()
    {
        if (ReferenceEquals(_boundDocument, _host.Document)) return;
        _boundDocument = _host.Document;
        _boundDocument.GetElementById("summon")!.AddEventListener("click", new DelegateEventListener(_ => Summon(1)));
        _boundDocument.GetElementById("banish")!.AddEventListener("click", new DelegateEventListener(_ => Summon(-_summoned)));
        Summon(0);
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

    private void Summon(int delta)
    {
        _summoned = Math.Max(0, _summoned + delta);
        _host.Document.GetElementById("count")!.TextContent = _summoned == 0
            ? "Nothing stirs."
            : _summoned == 1 ? "One shape stirs beneath the page." : _summoned + " shapes stir beneath the page.";
    }

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
        GetTree().Quit(count.StartsWith("One shape", StringComparison.Ordinal) ? 0 : 1);
    }
}
