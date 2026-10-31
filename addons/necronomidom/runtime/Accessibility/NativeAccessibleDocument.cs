using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Accessibility;
using NecronomiDom.Dom;
using NecronomiDom.Paint;
using Server = Godot.AccessibilityServer;
using Node = NecronomiDom.Dom.Node;

namespace NecronomiDom.Runtime;

/// <summary>Real sub-elements, never replacement Controls. Writes occur only inside the engine AX update notification.</summary>
internal sealed class NativeAccessibleDocument : IDisposable
{
    // Mixed checked and typed invalid states use AccessibilityServer methods that stock Godot 4.7 does not bind.
    // They are reported when the running engine provides them; otherwise screen readers see neither state.
    private static readonly StringName UpdateSetCheckedMixed = "update_set_checked_mixed";
    private static readonly StringName UpdateSetInvalidState = "update_set_invalid_state";
    private static readonly bool HasCheckedMixed = ClassDB.ClassHasMethod("AccessibilityServer", UpdateSetCheckedMixed);
    private static readonly bool HasInvalidState = ClassDB.ClassHasMethod("AccessibilityServer", UpdateSetInvalidState);
    private const int InvalidFalse = 0, InvalidTrue = 1, InvalidGrammar = 2, InvalidSpelling = 3;
    private static GodotObject ServerObject => Engine.GetSingleton("AccessibilityServer");
    private sealed class Entry
    {
        public readonly AccessibleNode Node;
        public Rid Rid;
        public Rid TextRun;
        public long Parent;
        public int Index;
        public int Revision = -1;
        public Rect2 Bounds;
        public bool GeometryDirty = true;
        public readonly Callable Focus;
        public readonly Callable Blur;
        public readonly Callable Click;
        public Entry(AccessibleNode node)
        {
            Node = node;
            if (node.Source is HTMLElement html)
            {
                Focus = Callable.From<Variant>(_ => { if (html.IsConnected && node.Exposed) html.Focus(); });
                Blur = Callable.From<Variant>(_ => { if (html.IsConnected && node.Exposed) html.Blur(); });
                Click = Callable.From<Variant>(_ => { if (html.IsConnected && node.Exposed && !node.States.Disabled) html.Click(); });
            }
        }
    }
    private readonly Dictionary<long, Entry> _entries = new();
    private readonly Dictionary<Element, Rect2> _geometry = new();
    private readonly HashSet<long> _retire = new();
    private readonly List<long> _retired = new();
    private AccessibleTree _tree;
    private bool _pending = true;
    private Rid _host;
    private readonly Callable _documentFocus;
    public NativeAccessibleDocument(AccessibleTree tree, Callable documentFocus)
    {
        _tree = tree;
        _documentFocus = documentFocus;
    }
    public int ElementCount => _entries.Count;
    public int PublishedUpdates { get; private set; }
    public Rid Focused(Element? focused, Rid host)
    {
        if (focused != null && _tree.Get(focused) is { } node)
        {
            if (node.Relations.TryGetValue("activedescendant", out var descendants) && descendants.Count != 0 && _entries.TryGetValue(descendants[0].Identity, out var active) && active.Rid.IsValid) return active.Rid;
            if (_entries.TryGetValue(node.Identity, out var entry) && entry.Rid.IsValid) return entry.Rid;
        }
        return host;
    }
    public Rid Get(Node source) => ReferenceEquals(source, _tree.Root.Source) ? _host
        : _tree.Get(source) is { } ax && _entries.TryGetValue(ax.Identity, out var entry) ? entry.Rid : default;
    /// <summary>Free only outside the update callback, as REQUIRED by the public AccessKit implementation.</summary>
    public bool Prepare(DisplayList list, bool geometryChanged)
    {
        bool changed = _tree.Synchronize();
        if (geometryChanged)
        {
            _geometry.Clear();
            foreach (var box in list.Boxes)
            {
                var bounds = new Rect2((float)box.Bounds.X, (float)box.Bounds.Y, (float)box.Bounds.Width, (float)box.Bounds.Height);
                _geometry[box.Element] = _geometry.TryGetValue(box.Element, out var previous) ? previous.Merge(bounds) : bounds;
            }
        }
        _retire.Clear();
        foreach (var node in _tree.Removed) _retire.Add(node.Identity);
        foreach (var node in _tree.Nodes)
        {
            if (ReferenceEquals(node, _tree.Root)) continue;
            long parent = node.Parent?.Identity ?? _tree.Root.Identity;
            int index = node.Parent?.Children.IndexOfReference(node) ?? 0;
            if (!_entries.TryGetValue(node.Identity, out var entry)) _entries[node.Identity] = entry = new Entry(node);
            else if (entry.Parent != parent || entry.Index != index) _retire.Add(node.Identity);
            entry.Parent = parent; entry.Index = index;
            if (geometryChanged)
            {
                Node? source = node.Source;
                if (source is Text) source = source.ParentNode;
                Rect2 bounds = source is Element e && _geometry.TryGetValue(e, out var rect) ? rect : default;
                if (entry.Bounds != bounds) { entry.Bounds = bounds; entry.GeometryDirty = true; changed = true; }
            }
            if (entry.TextRun.IsValid && (!entry.Bounds.HasArea() || node.Name.Length == 0))
            {
                if (Server.HasElement(entry.TextRun)) Server.FreeElement(entry.TextRun);
                entry.TextRun = default; entry.GeometryDirty = true; changed = true;
            }
            changed |= entry.Revision != node.Revision || !entry.Rid.IsValid;
        }
        // Parent deletion recursively frees descendants. Retire descendants before clearing stale handles.
        bool more;
        do
        {
            more = false;
            foreach (var pair in _entries) if (_retire.Contains(pair.Value.Parent) && _retire.Add(pair.Key)) more = true;
        } while (more);
        foreach (long id in _retire)
            if (_entries.TryGetValue(id, out var entry) && entry.Rid.IsValid && Server.HasElement(entry.Rid)) Server.FreeElement(entry.Rid);
        _retired.Clear();
        foreach (long id in _retire)
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.Rid = default; entry.TextRun = default; entry.Revision = -1;
                if (!entry.Node.Exposed) _retired.Add(id);
            }
        foreach (long id in _retired) _entries.Remove(id);
        _pending |= changed || _retire.Count != 0;
        return _pending;
    }
    public void Publish(Rid host)
    {
        if (!host.IsValid || !_pending) return;
        _host = host;
        Server.UpdateSetRole(host, Server.AccessibilityRole.Panel);
        Server.UpdateSetRoleDescription(host, "document");
        Server.UpdateSetName(host, _tree.Root.Name);
        Server.UpdateAddAction(host, Server.AccessibilityAction.Focus, _documentFocus);
        foreach (var node in _tree.Nodes)
        {
            if (ReferenceEquals(node, _tree.Root)) continue;
            Entry entry = _entries[node.Identity];
            EnsureElement(entry, host);
        }
        foreach (var node in _tree.Nodes)
        {
            if (ReferenceEquals(node, _tree.Root)) continue;
            Entry entry = _entries[node.Identity];
            if (entry.Revision == node.Revision && !entry.GeometryDirty) continue;
            PublishEntry(entry); entry.Revision = node.Revision; entry.GeometryDirty = false; PublishedUpdates++;
        }
        _pending = false;
    }
    private Rid EnsureElement(Entry entry, Rid host)
    {
        if (entry.Rid.IsValid) return entry.Rid;
        Rid parent = entry.Parent == _tree.Root.Identity ? host : EnsureElement(_entries[entry.Parent], host);
        // DOM order differs from AX parent order after aria-owns. Clamp insertion while later siblings are uncreated.
        int preceding = 0;
        if (entry.Node.Parent is { } owner)
            for (int i = 0; i < entry.Index; i++)
                if (_entries.TryGetValue(owner.Children[i].Identity, out var sibling) && sibling.Rid.IsValid) preceding++;
        entry.Rid = Server.CreateSubElement(parent, NativeAccessibleRoles.Map(entry.Node.Role), preceding);
        if (!entry.Rid.IsValid) throw new InvalidOperationException("AccessibilityServer rejected DOM sub-element " + entry.Node.Identity);
        entry.Revision = -1; return entry.Rid;
    }
    private void PublishEntry(Entry entry)
    {
        AccessibleNode node = entry.Node; Rid id = entry.Rid; var states = node.States;
        var role = node.Source is HTMLTextAreaElement ? Server.AccessibilityRole.MultilineTextField : NativeAccessibleRoles.Map(node.Role);
        Server.UpdateSetRole(id, role);
        Server.UpdateSetName(id, node.Name); Server.UpdateSetDescription(id, node.Description);
        Server.UpdateSetRoleDescription(id, NativeAccessibleRoles.Description(node));
        Server.UpdateSetLive(id, node.Live switch { AccessibleLive.Assertive => Server.AccessibilityLiveMode.Assertive, AccessibleLive.Polite => Server.AccessibilityLiveMode.Polite, _ => Server.AccessibilityLiveMode.Off });
        Server.UpdateSetBounds(id, entry.Bounds);
        Server.UpdateSetFlag(id, Server.AccessibilityFlags.Disabled, states.Disabled);
        Server.UpdateSetFlag(id, Server.AccessibilityFlags.Readonly, states.ReadOnly);
        Server.UpdateSetFlag(id, Server.AccessibilityFlags.Required, states.Required);
        Server.UpdateSetFlag(id, Server.AccessibilityFlags.Busy, states.Busy);
        Server.UpdateSetFlag(id, Server.AccessibilityFlags.Modal, states.Modal);
        Server.UpdateSetFlag(id, Server.AccessibilityFlags.Multiselectable, states.Multiselectable);
        AccessibleChecked toggled = states.Checked != AccessibleChecked.Undefined ? states.Checked : states.Pressed;
        if (toggled == AccessibleChecked.Mixed) { if (HasCheckedMixed) ServerObject.Call(UpdateSetCheckedMixed, id); }
        else if (toggled is AccessibleChecked.True or AccessibleChecked.False) Server.UpdateSetChecked(id, toggled == AccessibleChecked.True);
        if (HasInvalidState) ServerObject.Call(UpdateSetInvalidState, id, states.Invalid switch
        {
            "grammar" => InvalidGrammar,
            "spelling" => InvalidSpelling,
            null or "" or "false" => InvalidFalse,
            _ => InvalidTrue
        });
        if (states.Selected is { } selected) Server.UpdateSetListItemSelected(id, selected);
        if (states.Expanded is { } expanded) Server.UpdateSetListItemExpanded(id, expanded);
        if (node.Level is { } level) Server.UpdateSetListItemLevel(id, level);
        if (states.PositionInSet is { } position) Server.UpdateSetListItemIndex(id, position - 1);
        if (states.SetSize is { } size) Server.UpdateSetListItemCount(id, size);
        if (states.ValueNow is { } now) Server.UpdateSetNumValue(id, now);
        if (states.ValueMin is { } min && states.ValueMax is { } max) Server.UpdateSetNumRange(id, min, max);
        // Godot StaticText maps to AccessKit Label: its native accessible name comes from value, not label.
        Server.UpdateSetValue(id, role == Server.AccessibilityRole.StaticText ? node.Name : states.Value ?? "");
        // Actual exposed DOM text with retained visible layout owns a native text run.
        // AccessKit requires this real descendant for the parent's Text interface and typed invalid attributes.
        if (node.Source is Text && entry.Bounds.HasArea() && node.Name.Length != 0)
        {
            if (!entry.TextRun.IsValid) entry.TextRun = Server.CreateSubElement(id, Server.AccessibilityRole.TextRun);
            if (!entry.TextRun.IsValid) throw new InvalidOperationException("AccessibilityServer rejected rendered DOM text run " + node.Identity);
            Server.UpdateSetValue(entry.TextRun, node.Name);
            Server.UpdateSetBounds(entry.TextRun, entry.Bounds);
        }
        if (states.Orientation is "horizontal" or "vertical") Server.UpdateSetListOrientation(id, states.Orientation == "vertical");
        if (states.HasPopup is "menu" or "dialog" or "tree" or "listbox" or "grid") Server.UpdateSetPopupType(id, states.HasPopup switch { "dialog" => Server.AccessibilityPopupType.Dialog, "tree" => Server.AccessibilityPopupType.Tree, "listbox" or "grid" => Server.AccessibilityPopupType.List, _ => Server.AccessibilityPopupType.Menu });
        if (node.Source is Element e)
        {
            Server.UpdateSetPlaceholder(id, e.GetAttribute("placeholder") ?? "");
            Server.UpdateSetShortcut(id, e.GetAttribute("aria-keyshortcuts") ?? "");
            if (e.LocalName == "a") Server.UpdateSetUrl(id, e.GetAttribute("href") ?? "");
        }
        foreach (var relation in node.Relations)
            foreach (var target in relation.Value)
                if (_entries.TryGetValue(target.Identity, out var related) && related.Rid.IsValid)
                    switch (relation.Key)
                    {
                        case "labelledby": Server.UpdateAddRelatedLabeledBy(id, related.Rid); break;
                        case "describedby": Server.UpdateAddRelatedDescribedBy(id, related.Rid); break;
                        case "controls": Server.UpdateAddRelatedControls(id, related.Rid); break;
                        case "details": Server.UpdateAddRelatedDetails(id, related.Rid); break;
                        case "flowto": Server.UpdateAddRelatedFlowTo(id, related.Rid); break;
                        case "activedescendant": Server.UpdateSetActiveDescendant(id, related.Rid); break;
                        case "errormessage": Server.UpdateSetErrorMessage(id, related.Rid); break;
                    }
        if (node.Source is HTMLElement)
        {
            if (states.Focusable) { Server.UpdateAddAction(id, Server.AccessibilityAction.Focus, entry.Focus); Server.UpdateAddAction(id, Server.AccessibilityAction.Blur, entry.Blur); }
            if (!states.Disabled && node.Role is "button" or "link" or "checkbox" or "radio" or "switch" or "option" or "tab" or "menuitem") Server.UpdateAddAction(id, Server.AccessibilityAction.Click, entry.Click);
        }
    }
    public void Invalidate()
    {
        foreach (var entry in _entries.Values) { entry.Rid = default; entry.TextRun = default; entry.Revision = -1; }
        _host = default;
        _pending = true;
    }
    public void Dispose()
    {
        foreach (var entry in _entries.Values) if (entry.Rid.IsValid && Server.HasElement(entry.Rid)) Server.FreeElement(entry.Rid);
        _entries.Clear(); _geometry.Clear(); _retire.Clear(); _retired.Clear();
        _host = default;
    }
}
internal static class AccessibleChildList
{
    public static int IndexOfReference(this IReadOnlyList<AccessibleNode> list, AccessibleNode node)
    { for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], node)) return i; return -1; }
}
