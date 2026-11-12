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

    [Signal(Name = "dom_event_detail")]
    public delegate void DomEventDetailEventHandler(string type, string elementId, GodotDictionary details);

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

    partial void OnDocumentResourcesWithdrawn()
    {
        if (LiveHosts.Count != 0) return;
        // NativeAOT has no engine-driven finalization at shutdown. Run after paint/input/font/image
        // owners are withdrawn, not during DocumentWithdrawing while those owners still retain wrappers.
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
        string id = NearestId(e.Target as Dom.Node);
        if (HasConnections(SignalName.DomEventDetail)) DispatchScriptDetails(type, id, e);
        if (type == "click") EmitSignalClicked(id);
        else EmitSignalDomEvent(type, id);
    }

    private void DispatchScriptDetails(string type, string id, Event e)
    {
        using var details = new GodotDictionary
        {
            ["target_id"] = id,
            ["shift_key"] = _scriptShift, ["ctrl_key"] = _scriptCtrl,
            ["alt_key"] = _scriptAlt, ["meta_key"] = _scriptMeta
        };
        if (e is MouseEvent mouse)
        {
            details["client_x"] = mouse.ClientX; details["client_y"] = mouse.ClientY;
            details["position"] = new Vector2((float)mouse.ClientX, (float)mouse.ClientY);
            details["button"] = mouse.Button; details["buttons"] = mouse.Buttons;
        }
        if (e is WheelEvent wheel)
        {
            details["delta_x"] = wheel.DeltaX; details["delta_y"] = wheel.DeltaY;
            details["delta_mode"] = (long)wheel.DeltaMode;
        }
        if (e is PointerEvent pointer)
        {
            details["pointer_id"] = pointer.PointerId; details["pointer_type"] = pointer.PointerType;
            details["is_primary"] = pointer.IsPrimary;
        }
        if (e is KeyboardEvent key)
        {
            details["key"] = key.Key; details["code"] = key.Code; details["repeat"] = key.Repeat;
            details["shift_key"] = key.ShiftKey; details["ctrl_key"] = key.CtrlKey;
            details["alt_key"] = key.AltKey; details["meta_key"] = key.MetaKey;
        }
        switch (e.Target)
        {
            case HTMLInputElement input:
                details["value"] = input.Value; details["checked"] = input.Checked; break;
            case HTMLTextAreaElement area: details["value"] = area.Value; break;
            case HTMLSelectElement select:
                details["value"] = select.Value; details["selected_index"] = select.SelectedIndex; break;
        }
        // Build the snapshot before application callbacks can mutate or withdraw the document.
        EmitSignalDomEventDetail(type, id, details);
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
        HTMLSelectElement select => select.Value,
        var other => throw new ArgumentException("Not a text control: " + selector + " is <" + other.LocalName + ">.")
    };

    [BindMethod(Name = "set_value")]
    public void SetValue(string selector, string value)
    {
        switch (Find(selector))
        {
            case HTMLInputElement input: input.Value = value; break;
            case HTMLTextAreaElement area: area.Value = value; break;
            case HTMLSelectElement select: select.Value = value; break;
            case var other: throw new ArgumentException("Not a text control: " + selector + " is <" + other.LocalName + ">.");
        }
    }

    [BindMethod(Name = "focus_element")]
    public void FocusElement(string selector)
    {
        if (Find(selector) is HTMLElement html) html.Focus();
    }

    /// <summary>Activates the element as the DOM <c>click()</c> method does (emits <c>clicked</c> like a real click).</summary>
    [BindMethod(Name = "click")]
    public void ClickElement(string selector)
    {
        if (Find(selector) is HTMLElement html) html.Click();
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

    private bool _scriptShift, _scriptCtrl, _scriptAlt, _scriptMeta;

    partial void ObserveScriptModifiers(Godot.InputEvent input)
    {
        if (input is not InputEventWithModifiers modifiers) return;
        _scriptShift = modifiers.ShiftPressed; _scriptCtrl = modifiers.CtrlPressed;
        _scriptAlt = modifiers.AltPressed; _scriptMeta = modifiers.MetaPressed;
    }

    private static string NearestId(Dom.Node? node)
    {
        for (; node != null; node = node.ParentNode)
            if (node is Element { Id.Length: > 0 } element) return element.Id;
        return "";
    }

    private static DocumentFragment ParseFragment(Element context, string html) =>
        NecronomiDom.Html.HtmlImporter.ImportFragment(html, context, "gdscript:fragment").Fragment;

    [BindMethod(Name = "append_html")]
    public void AppendHtml(string selector, string html)
    {
        var parent = Find(selector);
        parent.AppendChild(ParseFragment(parent, html));
    }

    [BindMethod(Name = "set_inner_html")]
    public void SetInnerHtml(string selector, string html)
    {
        var parent = Find(selector);
        var fragment = ParseFragment(parent, html);
        while (parent.FirstChild is { } child) parent.RemoveChild(child);
        parent.AppendChild(fragment);
    }

    [BindMethod(Name = "insert_html_before")]
    public void InsertHtmlBefore(string selector, string html)
    {
        var target = Find(selector);
        var parent = target.ParentNode as Element ?? throw new ArgumentException("Target has no element parent.");
        parent.InsertBefore(ParseFragment(parent, html), target);
    }

    [BindMethod(Name = "remove_element")]
    public void RemoveElement(string selector)
    {
        var target = Find(selector);
        target.ParentNode?.RemoveChild(target);
    }

    [BindMethod(Name = "move_element")]
    public void MoveElement(string selector, string parentSelector, string beforeSelector = "")
    {
        var target = Find(selector);
        var parent = Find(parentSelector);
        var before = beforeSelector.Length == 0 ? null : Find(beforeSelector);
        if (ReferenceEquals(target.ParentNode, parent) &&
            (ReferenceEquals(target, before) || ReferenceEquals(target.NextSibling, before))) return;
        parent.InsertBefore(target, before);
    }

    [BindMethod(Name = "get_checked")]
    public bool GetChecked(string selector) => RequireInput(selector).Checked;
    [BindMethod(Name = "set_checked")]
    public void SetChecked(string selector, bool value) => RequireInput(selector).Checked = value;
    [BindMethod(Name = "get_selected_index")]
    public int GetSelectedIndex(string selector) => RequireSelect(selector).SelectedIndex;
    [BindMethod(Name = "set_selected_index")]
    public void SetSelectedIndex(string selector, int value) => RequireSelect(selector).SelectedIndex = value;
    private HTMLInputElement RequireInput(string selector) =>
        Find(selector) as HTMLInputElement ?? throw new ArgumentException("Not an input: " + selector);
    private HTMLSelectElement RequireSelect(string selector) =>
        Find(selector) as HTMLSelectElement ?? throw new ArgumentException("Not a select: " + selector);
    private HTMLElement RequireHtml(string selector) =>
        Find(selector) as HTMLElement ?? throw new ArgumentException("Not an HTML element: " + selector);

    [BindMethod(Name = "get_scroll")]
    public Vector2 GetScroll(string selector)
    {
        var element = Find(selector);
        return new Vector2((float)element.ScrollLeft, (float)element.ScrollTop);
    }
    [BindMethod(Name = "scroll_to")]
    public void ScrollTo(string selector, double x, double y) => Find(selector).ScrollTo(x, y);
    [BindMethod(Name = "get_element_rect")]
    public Rect2 GetElementRect(string selector)
    {
        var element = Find(selector);
        foreach (var box in DisplayList.Boxes)
            if (ReferenceEquals(box.Element, element))
            {
                var rect = box.VisualBounds;
                return new Rect2((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
            }
        return default;
    }
    [BindMethod(Name = "set_style")]
    public void SetStyle(string selector, string property, string value) => RequireHtml(selector).Style.SetProperty(property, value);
    [BindMethod(Name = "get_style")]
    public string GetStyle(string selector, string property) => RequireHtml(selector).Style.GetPropertyValue(property);
    [BindMethod(Name = "set_pointer_capture")]
    public void SetPointerCapture(string selector, int pointerId) => Find(selector).SetPointerCapture(pointerId);
    [BindMethod(Name = "release_pointer_capture")]
    public void ReleasePointerCapture(string selector, int pointerId) => Find(selector).ReleasePointerCapture(pointerId);
    [BindMethod(Name = "has_pointer_capture")]
    public bool HasPointerCapture(string selector, int pointerId) => Find(selector).HasPointerCapture(pointerId);
    [BindMethod(Name = "get_active_element_id")]
    public string GetActiveElementId() => Document?.ActiveElement?.Id ?? "";
    [BindMethod(Name = "element_from_point")]
    public string ElementFromPoint(double x, double y) => NearestId(DisplayList.HitTest(x, y));

    private Element Find(string selector) =>
        (Document ?? throw new InvalidOperationException("No document is loaded."))
            .QuerySelector(selector) ?? throw new ArgumentException("No element matches " + selector + ".");
}
