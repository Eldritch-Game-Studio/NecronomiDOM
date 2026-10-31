using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using NecronomiDom.Dom;

namespace NecronomiDom.Runtime;

/// <summary>
/// Script API of the GDExtension build. Elements are addressed with CSS selectors (first match); events report the
/// id of the nearest element (target or ancestor) that has one.
/// </summary>
public partial class DocumentHost
{
    /// <summary>A click on an element; <paramref name="elementId"/> is the nearest id ("" if none).</summary>
    [Signal(Name = "clicked")]
    public delegate void ClickedEventHandler(string elementId);

    /// <summary>A DOM event of a type requested with <see cref="Listen"/>.</summary>
    [Signal(Name = "dom_event")]
    public delegate void DomEventEventHandler(string type, string elementId);

    /// <summary>Escape or the cancel action while the host accepts input.</summary>
    [Signal(Name = "cancel")]
    public delegate void CancelEventHandler();

    private readonly HashSet<string> _listenedTypes = new(StringComparer.Ordinal) { "click" };
    private readonly System.Collections.Generic.Dictionary<string, IEventListener> _scriptListeners = new(StringComparer.Ordinal);
    private Document? _scriptDocument;

    public DocumentHost() => CancelRequested += () => EmitSignalCancel();

    partial void OnDocumentLive()
    {
        if (ReferenceEquals(_scriptDocument, Document)) return;
        _scriptDocument = Document;
        _scriptListeners.Clear();
        foreach (var type in _listenedTypes) AttachScriptListener(type);
    }

    partial void OnDocumentWithdrawn()
    {
        if (LiveHosts.Count != 0) return;
        // NativeAOT has no engine-driven finalization at shutdown: when the last document goes, drop the shared shaders
        // and collect released paint wrappers so their engine resources are freed before the servers shut down.
        WebPaintShaders.Release();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void AttachScriptListener(string type)
    {
        if (_scriptDocument == null || _scriptListeners.ContainsKey(type)) return;
        var listener = new DelegateEventListener(e => DispatchScriptEvent(type, e));
        _scriptListeners.Add(type, listener);
        _scriptDocument.AddEventListener(type, listener, true);
    }

    private void DispatchScriptEvent(string type, Event e)
    {
        string id = "";
        for (var node = e.Target as Dom.Node; node != null; node = node.ParentNode)
            if (node is Element element && element.Id.Length > 0) { id = element.Id; break; }
        if (type == "click") EmitSignalClicked(id);
        else EmitSignalDomEvent(type, id);
    }

    /// <summary>Also report DOM events of <paramref name="type"/> (e.g. "input", "focusin", "pointerenter") through <c>dom_event</c>.</summary>
    [BindMethod(Name = "listen")]
    public void Listen(string type)
    {
        if (_listenedTypes.Add(type)) AttachScriptListener(type);
    }

    [BindMethod(Name = "load_document")]
    public void ScriptLoadDocument(string path) => LoadDocument(path);

    [BindMethod(Name = "has_element")]
    public bool HasElement(string selector) => Document?.QuerySelector(selector) != null;

    [BindMethod(Name = "get_text")]
    public string GetText(string selector) => Find(selector).TextContent ?? "";

    [BindMethod(Name = "set_text")]
    public void SetText(string selector, string text) => Find(selector).TextContent = text;

    [BindMethod(Name = "get_attribute")]
    public string GetAttribute(string selector, string name) => Find(selector).GetAttribute(name) ?? "";

    [BindMethod(Name = "set_attribute")]
    public void SetAttribute(string selector, string name, string value) => Find(selector).SetAttribute(name, value);

    [BindMethod(Name = "remove_attribute")]
    public void RemoveAttribute(string selector, string name) => Find(selector).RemoveAttribute(name);

    [BindMethod(Name = "has_class")]
    public bool HasClass(string selector, string className) => Find(selector).ClassList.Contains(className);

    [BindMethod(Name = "add_class")]
    public void AddClass(string selector, string className) => Find(selector).ClassList.Add(className);

    [BindMethod(Name = "remove_class")]
    public void RemoveClass(string selector, string className) => Find(selector).ClassList.Remove(className);

    [BindMethod(Name = "toggle_class")]
    public bool ToggleClass(string selector, string className) => Find(selector).ClassList.Toggle(className);

    /// <summary>Value of an <c>input</c> or <c>textarea</c>.</summary>
    [BindMethod(Name = "get_value")]
    public string GetValue(string selector) => Find(selector) switch
    {
        HTMLInputElement input => input.Value,
        HTMLTextAreaElement area => area.Value,
        var other => throw new ArgumentException("Not a text control: " + selector + " is <" + other.LocalName + ">.")
    };

    [BindMethod(Name = "set_value")]
    public void SetValue(string selector, string value)
    {
        switch (Find(selector))
        {
            case HTMLInputElement input: input.Value = value; break;
            case HTMLTextAreaElement area: area.Value = value; break;
            case var other: throw new ArgumentException("Not a text control: " + selector + " is <" + other.LocalName + ">.");
        }
    }

    [BindMethod(Name = "focus_element")]
    public void FocusElement(string selector)
    {
        if (Find(selector) is HTMLElement html) html.Focus();
    }

    /// <summary>Ids of every element matching <paramref name="selector"/> (elements without an id are skipped).</summary>
    [BindMethod(Name = "query_ids")]
    public PackedStringArray QueryIds(string selector)
    {
        var result = new PackedStringArray();
        if (Document == null) return result;
        foreach (var node in Document.QuerySelectorAll(selector))
            if (node is Element element && element.Id.Length > 0) result.Add(element.Id);
        return result;
    }

    [BindMethod(Name = "set_input_enabled")]
    public void SetInputEnabled(bool enabled) => InputEnabled = enabled;

    [BindMethod(Name = "is_input_enabled")]
    public bool IsInputEnabled() => InputEnabled;

    [BindMethod(Name = "set_application_navigation")]
    public void SetApplicationNavigation(bool enabled) => ApplicationNavigation = enabled;

    [BindMethod(Name = "apply_text_scale")]
    public void ScriptApplyTextScale(float scale) => ApplyTextScale(scale);

    private Element Find(string selector) =>
        (Document ?? throw new InvalidOperationException("No document is loaded."))
            .QuerySelector(selector) ?? throw new ArgumentException("No element matches " + selector + ".");
}
