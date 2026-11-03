#nullable enable
using System;
using Godot;
using NecronomiDom.Dom;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    private DocumentInspector? _developerInspector;
    internal DocumentInputInspection? InspectInputState() => _documentInput?.InspectState();

    /// <summary>Explicit developer opt-in. No overlay, input hook, timer or metrics subscription exists until attached.
    /// The returned child belongs to this host; freeing it detaches the inspector and restores input.</summary>
    public DocumentInspector AttachInspector()
    {
        if (!OS.IsDebugBuild()) throw new InvalidOperationException("The DOM inspector requires a debug engine/template.");
        if (_developerInspector != null && GodotObject.IsInstanceValid(_developerInspector)) return _developerInspector;
        var inspector = new DocumentInspector();
        inspector.Attach(this);
        _developerInspector = inspector;
        AddChild(inspector);
        return inspector;
    }

    public bool InspectorOpen => _developerInspector != null && GodotObject.IsInstanceValid(_developerInspector) && _developerInspector.IsOpen;
    public string InspectorSelection => InspectorOpen ? DocumentInspection.Label(_developerInspector!.SelectedElement) : "";
    [BindMethod(Name = "is_inspector_open")]
    public bool IsInspectorOpen() => InspectorOpen;
    [BindMethod(Name = "get_inspector_selection")]
    public string GetInspectorSelection() => InspectorSelection;
    [BindMethod(Name = "open_inspector")]
    public void OpenInspector() => AttachInspector().Open();
    [BindMethod(Name = "close_inspector")]
    public void CloseInspector() { if (InspectorOpen) _developerInspector!.Close(); }
    [BindMethod(Name = "toggle_inspector")]
    public void ToggleInspector() { if (InspectorOpen) CloseInspector(); else OpenInspector(); }
    [BindMethod(Name = "begin_inspector_pick")]
    public void BeginInspectorPick() { OpenInspector(); _developerInspector!.BeginPick(); }
    /// <summary>Coordinates are document-local CSS pixels, not viewport/window pixels.</summary>
    [BindMethod(Name = "pick_inspector_element")]
    public bool PickInspectorElement(double x, double y) { OpenInspector(); return _developerInspector!.Pick(x, y); }
    [BindMethod(Name = "select_inspector_element")]
    public bool SelectInspectorElement(string selector)
    {
        OpenInspector();
        Element? element = Document?.QuerySelector(selector);
        if (element == null) return false;
        _developerInspector!.Select(element);
        return true;
    }
    [BindMethod(Name = "get_inspector_report")]
    public string GetInspectorReport() => InspectorOpen ? _developerInspector!.CaptureReport() : "";
}
