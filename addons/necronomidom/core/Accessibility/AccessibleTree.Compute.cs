using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Dom;

namespace NecronomiDom.Accessibility;

public sealed partial class AccessibleTree
{
    private readonly HashSet<Node> _namePath = new();
    private readonly List<AccessibleNode> _relationScratch = new();
    private void Compute(AccessibleNode ax)
    {
        _computing = ax.Source; ClearDependencies(ax.Source);
        string role, name, description = "", roleDescription = "";
        int? level = null; AccessibleLive live = AccessibleLive.Off; AccessibleStates states = default;
        bool liveAtomic = false; string liveRelevant = "additions text";
        if (ax.Ignored) { role = "none"; name = ""; }
        else if (ax.Source is Element e)
        {
            role = AccessibleRoles.Compute(e, Focusable(e));
            _namePath.Clear(); name = AccessibleRoles.NameProhibited(role) ? "" : TextAlternative(e, false, false, false, false);
            _namePath.Clear(); description = Description(e, name);
            roleDescription = e.GetAttribute("aria-roledescription") ?? "";
            level = AccessibleRoles.Integer(e.GetAttribute("aria-level"));
            if (level <= 0) level = null;
            if (role == "heading") level ??= e.LocalName.Length == 2 && e.LocalName[0] == 'h' && e.LocalName[1] is >= '1' and <= '6' ? e.LocalName[1] - '0' : 2;
            if (role is "listitem" or "treeitem") level ??= NativeLevel(e, role);
            live = Live(e); states = States(e, role);
            (liveAtomic, liveRelevant) = LiveMetadata(e);
            Relations(ax, e);
        }
        else if (ax.Source is Text text)
        {
            role = "StaticText"; name = Flat(text.Data);
            if (text.ParentElement is { } parent)
            { live = Live(parent); (liveAtomic, liveRelevant) = LiveMetadata(parent); }
        }
        else
        {
            role = "RootWebArea"; name = Flat(_document.Title);
            states = default(AccessibleStates) with { Focusable = true, Focused = _document.FocusedElement == null };
        }
        if (ax.Role != role || ax.Name != name || ax.Description != description || ax.RoleDescription != roleDescription || ax.Level != level || ax.Live != live || ax.States != states
            || ax.LiveAtomic != liveAtomic || ax.LiveRelevant != liveRelevant)
        {
            ax.Role = role; ax.Name = name; ax.Description = description; ax.RoleDescription = roleDescription;
            ax.Level = level; ax.Live = live; ax.States = states; ChangedNode(ax);
            ax.LiveAtomic = liveAtomic; ax.LiveRelevant = liveRelevant;
        }
        _computing = null;
    }
    private string Description(Element e, string name)
    {
        var builder = new StringBuilder(); bool found = false;
        foreach (string id in AccessibleRoles.Tokens(e.GetAttribute("aria-describedby")))
            if (Resolve(e, id) is { } target) { found = true; Add(builder, TextAlternative(target, true, Hidden(target), false, true)); }
        if (found) return Flat(builder.ToString());
        if (e.GetAttribute("aria-description") is { } direct) return Flat(direct);
        string title = Flat(e.GetAttribute("title") ?? "");
        return title == name ? "" : title;
    }
    private string TextAlternative(Node source, bool referenced, bool includeHidden, bool inLabelledBy, bool contents)
    {
        Depend(source);
        if (!includeHidden && Hidden(source)) return "";
        if (!_namePath.Add(source)) return "";
        try
        {
            if (source is Text text) return text.Data;
            if (source is not Element e) return Contents(source, includeHidden, inLabelledBy);
            if (!inLabelledBy)
            {
                var builder = new StringBuilder(); bool found = false;
                foreach (string id in AccessibleRoles.Tokens(e.GetAttribute("aria-labelledby")))
                    if (Resolve(e, id) is { } target)
                    {
                        found = true;
                        // Self references must continue with aria-label/native/content, not terminate as a cycle.
                        bool self = ReferenceEquals(target, source);
                        if (self) _namePath.Remove(source);
                        Add(builder, TextAlternative(target, true, Hidden(target), true, true));
                        if (self) _namePath.Add(source);
                    }
                if (found) return Flat(builder.ToString());
            }
            // Embedded controls contribute current values to a label/name-from-content.
            if (contents && !referenced)
            {
                if (e is HTMLInputElement input && input.Type is not ("button" or "submit" or "reset" or "checkbox" or "radio" or "hidden")) return input.Value;
                if (e is HTMLTextAreaElement area) return area.Value;
                if (e is HTMLSelectElement select)
                { var result = new StringBuilder(); foreach (var option in select.Options) if (option is HTMLOptionElement { Selected: true } o) Add(result, TextAlternative(o, false, includeHidden, inLabelledBy, true)); return result.ToString(); }
                string embeddedRole = AccessibleRoles.Compute(e, Focusable(e));
                if (embeddedRole is "slider" or "spinbutton" or "scrollbar" or "progressbar") return e.GetAttribute("aria-valuetext") ?? e.GetAttribute("aria-valuenow") ?? "";
            }
            if (!AccessibleRoles.NameProhibited(AccessibleRoles.Compute(e, Focusable(e))) && e.GetAttribute("aria-label") is { } label && !string.IsNullOrWhiteSpace(label)) return Flat(label);
            if (e.LocalName is "img" or "area" && e.GetAttribute("alt") is { } alt) return Flat(alt);
            if (e is HTMLInputElement button && button.Type is "button" or "submit" or "reset")
                return Flat(button.GetAttribute("value") ?? (button.Type == "submit" ? "Submit" : button.Type == "reset" ? "Reset" : ""));
            if (!contents && IsLabelable(e))
            {
                var labels = new StringBuilder(); bool found = false;
                foreach (Node n in e.Root.InclusiveDescendants())
                    if (n is Element { LocalName: "label" } l && LabelControl(l) is { } control && ReferenceEquals(control, e))
                    { found = true; Depend(l); Add(labels, TextAlternative(l, true, Hidden(l), inLabelledBy, true)); }
                if (found) return Flat(labels.ToString());
            }
            string? childTag = e.LocalName switch { "fieldset" => "legend", "table" => "caption", "figure" => "figcaption", "svg" => "title", _ => null };
            if (childTag != null)
                for (var child = e.FirstChild; child != null; child = child.NextSibling)
                    if (child is Element ce && ce.LocalName == childTag) return Flat(TextAlternative(ce, true, Hidden(ce), inLabelledBy, true));
            if (e.LocalName == "optgroup" && e.GetAttribute("label") is { } groupLabel) return Flat(groupLabel);
            if (e is HTMLOptionElement && e.GetAttribute("label") is { Length: > 0 } optionLabel) return Flat(optionLabel);
            string role = AccessibleRoles.Compute(e, Focusable(e));
            if (referenced || contents || AccessibleRoles.NameFromContent(role))
            {
                string result = Flat(Contents(e, includeHidden, inLabelledBy));
                if (result.Length != 0) return result;
            }
            if (e.GetAttribute("title") is { Length: > 0 } title) return Flat(title);
            if (role is "textbox" or "searchbox" && e.GetAttribute("placeholder") is { } placeholder) return Flat(placeholder);
            return "";
        }
        finally { _namePath.Remove(source); }
    }
    private string Contents(Node source, bool includeHidden, bool inLabelledBy)
    {
        var builder = new StringBuilder();
        foreach (Node child in ComposedChildren(source))
        {
            bool block = child is Element e && (_display != null
                ? _display(e) is not ("inline" or "inline-block" or "inline-flex" or "inline-grid" or "contents" or "none")
                : e.LocalName is "div" or "p" or "br" or "li" or "section" or "article" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6");
            if (block && builder.Length != 0) builder.Append(' ');
            builder.Append(TextAlternative(child, false, includeHidden, inLabelledBy, true));
            if (block) builder.Append(' ');
        }
        if (source is Element owner)
            foreach (string id in AccessibleRoles.Tokens(owner.GetAttribute("aria-owns")))
                if (Resolve(owner, id) is { } target)
                {
                    bool composed = false;
                    foreach (Node child in ComposedChildren(source)) if (ReferenceEquals(child, target)) { composed = true; break; }
                    if (composed || _nodes.TryGetValue(owner, out var ax) && !ReferenceEquals(Get(target)?.Parent, ax)) continue;
                    Add(builder, TextAlternative(target, false, includeHidden, inLabelledBy, true));
                }
        return builder.ToString();
    }
    private Element? LabelControl(Element label)
    {
        Depend(label);
        if (label.GetAttribute("for") is { } id) return Resolve(label, id) is { } target && IsLabelable(target) ? target : null;
        foreach (var n in label.InclusiveDescendants()) if (n is Element e && !ReferenceEquals(e, label) && IsLabelable(e)) return e;
        return null;
    }
    private static bool IsLabelable(Element e) => e.LocalName is "button" or "meter" or "output" or "progress" or "select" or "textarea" || e is HTMLInputElement { Type: not "hidden" };
    private static void Add(StringBuilder builder, string value) { if (value.Length == 0) return; if (builder.Length != 0) builder.Append(' '); builder.Append(value); }
    private static string Flat(string value) => HtmlText.CollapseWhitespace(value);
    private int NativeLevel(Element e, string role)
    {
        int level = role == "listitem" ? 0 : 1;
        for (Node? n = e.ParentNode; n != null; n = n.ParentNode)
            if (n is Element ancestor) { Depend(n); if (role == "listitem" && ancestor.LocalName is "ul" or "ol" || role == "treeitem" && ancestor.GetAttribute("role") == "group") level++; }
        return Math.Max(1, level);
    }
    private static readonly string[] RelationNames = { "labelledby", "describedby", "controls", "details", "errormessage", "flowto", "owns", "activedescendant" };
    private void Relations(AccessibleNode ax, Element e)
    {
        foreach (string name in RelationNames)
        {
            if (!ax.MutableRelations.TryGetValue(name, out var list)) ax.MutableRelations[name] = list = new();
            var resolved = _relationScratch; resolved.Clear();
            foreach (string id in AccessibleRoles.Tokens(e.GetAttribute("aria-" + name)))
                if (Resolve(e, id) is { } target) { Depend(target); var related = Identity(target); if (!resolved.Contains(related)) resolved.Add(related); }
            if (name == "labelledby" && resolved.Count == 0 && IsLabelable(e))
                foreach (Node n in e.Root.InclusiveDescendants())
                    if (n is Element { LocalName: "label" } label && ReferenceEquals(LabelControl(label), e) && _nodes.TryGetValue(label, out var related))
                    { Depend(label); resolved.Add(related); }
            bool changed = list.Count != resolved.Count;
            if (!changed) for (int i = 0; i < list.Count; i++) if (!ReferenceEquals(list[i], resolved[i])) { changed = true; break; }
            if (changed) { list.Clear(); list.AddRange(resolved); ChangedNode(ax); }
        }
    }
}
