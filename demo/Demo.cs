using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Bindings;
using NecronomiDom.Dom;
using NecronomiDom.Runtime;

namespace NecronomiDomDemo;

/// <summary>An authored document, a C# model, typed one-way bindings and ordinary event handlers.</summary>
public partial class Demo : Godot.Node
{
    private DocumentHost _host = null!;
    private readonly WorkbenchViewModel _model = new(OS.IsDebugBuild());
    private DocumentBindings? _bindings;
    private readonly List<(Element Element, string Type, DelegateEventListener Listener)> _listeners = new();

    public override void _Ready()
    {
        _host = GetNode<DocumentHost>("UI/Document");
        bool proof = Array.IndexOf(OS.GetCmdlineUserArgs(), "--workbench-smoke") >= 0;
        if (proof) PrepareProofSources();
        // Manual commands need a baseline, not an automatic poll. Watching is a separate explicit opt-in.
        _host.DeveloperReloadWatchEnabled = false;
        _host.ReloadCompleted += OnReloadCompleted;
        _host.DeveloperReloadEnabled = _model.DeveloperAvailable;
        BindDocument();
        _host.CancelRequested += OnCancelRequested;
        if (proof || Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0) RunSmoke(proof);
    }

    private void BindDocument()
    {
        if (_bindings is { IsDisposed: false }) return;
        var bindings = _host.CreateBindings();
        _bindings = bindings;
        bindings.Disposed += DetachDocument;
        try
        {
            bindings.Text("#character-preview", _model, nameof(WorkbenchViewModel.CharacterPreview), model => model.CharacterPreview);
            bindings.Text("#loadout-text", _model, nameof(WorkbenchViewModel.LoadoutText), model => model.LoadoutText);
            bindings.Text("#equip", _model, nameof(WorkbenchViewModel.EquipLabel), model => model.EquipLabel);
            bindings.Text("#profile-status", _model, nameof(WorkbenchViewModel.ProfileStatus), model => model.ProfileStatus);
            bindings.Enabled("#save-profile", _model, nameof(WorkbenchViewModel.CanSave), model => model.CanSave);
            bindings.Class("#character-card", "editing", _model, nameof(WorkbenchViewModel.IsEditing), model => model.IsEditing);
            bindings.Class("#loadout", "ready", _model, nameof(WorkbenchViewModel.LanternEquipped), model => model.LanternEquipped);
            bindings.Visible("#lantern-hint", _model, nameof(WorkbenchViewModel.LanternEquipped), model => model.LanternEquipped);
            bindings.Text("#supply-count", _model, nameof(WorkbenchViewModel.SupplyText), model => model.SupplyText);
            bindings.Visible("#tracker", _model, nameof(WorkbenchViewModel.HasQuest), model => model.HasQuest);
            bindings.Visible("#empty-tracker", _model, nameof(WorkbenchViewModel.NoQuest), model => model.NoQuest);
            bindings.Text("#tracker-title", _model, nameof(WorkbenchViewModel.TrackerTitle), model => model.TrackerTitle);
            bindings.Text("#tracker-progress", _model, nameof(WorkbenchViewModel.TrackerProgress), model => model.TrackerProgress);
            bindings.Text("#tracker-hint", _model, nameof(WorkbenchViewModel.TrackerHint), model => model.TrackerHint);
            bindings.Enabled("#record", _model, nameof(WorkbenchViewModel.CanRecord), model => model.CanRecord);
            bindings.Enabled("#claim", _model, nameof(WorkbenchViewModel.CanClaim), model => model.CanClaim);
            bindings.Class("#tracker", "complete", _model, nameof(WorkbenchViewModel.CanClaim), model => model.CanClaim);
            for (int i = 0; i < 3; i++)
            {
                int quest = i;
                string selector = "#accept-" + quest;
                // Derived values depend on the whole model, so null property notifications intentionally refresh them.
                bindings.Enabled(selector, _model, nameof(WorkbenchViewModel.HasQuest), model => model.CanAccept(quest));
                bindings.Text(selector, _model, nameof(WorkbenchViewModel.HasQuest), model => model.QuestLabel(quest));
                Listen("accept-" + quest, "click", () => _model.Accept(quest));
            }
            bindings.Text("#watch", _model, nameof(WorkbenchViewModel.WatchLabel), model => model.WatchLabel);
            bindings.Class("#watch", "selected", _model, nameof(WorkbenchViewModel.Watching), model => model.Watching);
            bindings.Text("#reload-status", _model, nameof(WorkbenchViewModel.ReloadStatus), model => model.ReloadStatus);
            bindings.Class("#reload-status", "error", _model, nameof(WorkbenchViewModel.ReloadFailed), model => model.ReloadFailed);
            bindings.Visible("#reference-panel", _model, nameof(WorkbenchViewModel.ReferenceVisible), model => model.ReferenceVisible);
            bindings.Visible("#release-notice", _model, nameof(WorkbenchViewModel.DeveloperAvailable), model => !model.DeveloperAvailable);
            foreach (string id in new[] { "inspect", "watch", "reload", "force-reload" })
                bindings.Enabled("#" + id, _model, nameof(WorkbenchViewModel.DeveloperAvailable), model => model.DeveloperAvailable);

            // Input is deliberately not a two-way binding: C# explicitly owns when edits reach the model.
            var name = (HTMLInputElement)Element("name");
            var motto = (HTMLInputElement)Element("motto");
            name.Value = _model.DraftName;
            motto.Value = _model.DraftMotto;
            Listen("name", "input", () => _model.EditName(name.Value));
            Listen("motto", "input", () => _model.EditMotto(motto.Value));
            Listen("equip", "click", _model.ToggleLantern);
            Listen("save-profile", "click", _model.SaveProfile);
            Listen("record", "click", _model.RecordFinding);
            Listen("claim", "click", _model.Claim);
            Listen("inspect", "click", ToggleInspector);
            Listen("watch", "click", ToggleWatching);
            Listen("reload", "click", () => _host.ReloadChangedFiles());
            Listen("force-reload", "click", () => _host.ForceReload());
            Listen("reference", "click", _model.ToggleReference);
            Listen("open-reference", "click", () => OpenFile("res://docs/capabilities.md"));
            Listen("open-css", "click", () => OpenFile(_host.DocumentPath[.._host.DocumentPath.LastIndexOf('/')] + "/workbench.css"));
        }
        catch (Exception error)
        {
            bindings.Dispose();
            GD.PushError("Workbench binding failed: " + error);
            throw;
        }
    }

    private Element Element(string id) => _host.Document.GetElementById(id)
        ?? throw new InvalidOperationException($"Workbench markup must retain id='{id}' for its C# handler/binding.");
    private void Listen(string id, string type, Action action)
    {
        var element = Element(id);
        var listener = new DelegateEventListener(_ => action());
        element.AddEventListener(type, listener);
        _listeners.Add((element, type, listener));
    }
    private void DetachDocument()
    {
        foreach (var (element, type, listener) in _listeners) element.RemoveEventListener(type, listener);
        _listeners.Clear();
        if (_bindings != null) _bindings.Disposed -= DetachDocument;
        _bindings = null;
    }
    private void OnReloadCompleted(DeveloperReloadResult result)
    {
        if (result.DocumentReplaced) BindDocument();
        string message = result.Kind switch
        {
            DeveloperReloadKind.Styles => "CSS applied · same document, input value, selection and focus retained.",
            DeveloperReloadKind.Document => "Markup replaced · C# model retained; inputs restored and controls rebound. Focus starts anew.",
            DeveloperReloadKind.Unchanged => "No source changes · edit the linked workbench.css, then reload changed files (F7).",
            DeveloperReloadKind.Failed => "Reload rejected · working UI retained. " + result.Error + " Fix the source, then try Reload changed again.",
            _ => "Developer tools require a debug engine/template."
        };
        _model.ShowReload(message, result.Kind == DeveloperReloadKind.Failed);
    }
    private void ToggleInspector() { if (_model.DeveloperAvailable) _host.ToggleInspector(); }
    private void ToggleWatching()
    {
        if (!_model.DeveloperAvailable) return;
        _host.DeveloperReloadWatchEnabled = !_host.DeveloperReloadWatchEnabled;
        _model.SetWatching(_host.DeveloperReloadWatchEnabled);
        _model.ShowReload(_model.Watching ? "Watching HTML + linked CSS every 0.5s. Toggle off to stop polling." : "Watching stopped · manual reload remains available.");
    }
    private void OpenFile(string path)
    {
        Error error = OS.ShellOpen(ProjectSettings.GlobalizePath(path));
        if (error != Error.Ok) _model.ShowReload($"Could not open {path}: {error}. Open this path in your editor instead.", true);
    }
    private static void OnCancelRequested() => GD.Print("cancel requested");
    public override void _ExitTree()
    {
        _host.ReloadCompleted -= OnReloadCompleted;
        _host.CancelRequested -= OnCancelRequested;
        _bindings?.Dispose();
        CleanProofSources();
    }
    public override void _Input(Godot.InputEvent input)
    {
        if (!_model.DeveloperAvailable || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.F6: ToggleInspector(); break;
            case Key.F7: _host.ReloadChangedFiles(); break;
            case Key.F8: _host.ForceReload(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
}
