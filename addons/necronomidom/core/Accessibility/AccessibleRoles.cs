using System;
using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Accessibility;

/// <summary>HTML-AAM mappings and non-abstract WAI-ARIA 1.2 roles. No Godot role vocabulary enters Core.</summary>
internal static class AccessibleRoles
{
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox",
        "code", "columnheader", "combobox", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory",
        "document", "emphasis", "feed", "figure", "form", "generic", "grid", "gridcell", "group", "heading", "img", "insertion",
        "link", "list", "listbox", "listitem", "log", "main", "marquee", "math", "menu", "menubar", "menuitem", "menuitemcheckbox",
        "menuitemradio", "meter", "navigation", "none", "note", "option", "paragraph", "presentation", "progressbar", "radio",
        "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar", "search", "searchbox", "separator", "slider",
        "spinbutton", "status", "strong", "subscript", "superscript", "switch", "tab", "table", "tablist", "tabpanel", "term",
        "textbox", "time", "timer", "toolbar", "tooltip", "tree", "treegrid", "treeitem"
    };
    public static bool NameFromContent(string role) => role is "button" or "cell" or "checkbox" or "columnheader" or "gridcell"
        or "heading" or "link" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "option" or "radio" or "row" or "rowheader"
        or "switch" or "tab" or "tooltip" or "treeitem";
    public static bool NameProhibited(string role) => role is "caption" or "code" or "deletion" or "emphasis" or "generic"
        or "insertion" or "none" or "paragraph" or "presentation" or "strong" or "subscript" or "superscript";
    public static string Compute(Element e, bool focusable)
    {
        string implicitRole = Implicit(e);
        foreach (var token in Tokens(e.GetAttribute("role")))
        {
            if (!Roles.Contains(token) || !Permitted(e, token)) continue;
            if (token is "none" or "presentation")
            {
                // ARIA presentational-role conflict resolution: focusability or global ARIA overrides presentation.
                if (focusable || HasGlobalAria(e)) return implicitRole;
                return "none";
            }
            return token;
        }
        return implicitRole;
    }
    private static readonly string[] GlobalAriaAttributes =
    {
        "aria-atomic", "aria-busy", "aria-controls", "aria-current", "aria-describedby", "aria-description",
        "aria-details", "aria-dropeffect", "aria-errormessage", "aria-flowto", "aria-grabbed", "aria-haspopup", "aria-hidden",
        "aria-invalid", "aria-keyshortcuts", "aria-label", "aria-labelledby", "aria-live", "aria-owns", "aria-relevant", "aria-roledescription"
    };
    private static bool HasGlobalAria(Element e)
    {
        foreach (var name in GlobalAriaAttributes)
            if (e.HasAttribute(name)) return true;
        return false;
    }
    private static bool Permitted(Element e, string role)
    {
        // ARIA in HTML: strong native semantics cannot be turned into an unrelated widget.
        return e.LocalName switch
        {
            "button" => role is "button" or "checkbox" or "combobox" or "link" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "option" or "radio" or "slider" or "switch" or "tab" or "treeitem",
            "select" => role is "combobox" or "listbox" or "menu",
            "textarea" => role == "textbox",
            "option" => role == "option",
            "progress" => role == "progressbar",
            "meter" => role == "meter",
            "input" when e is HTMLInputElement { Type: "hidden" } => false,
            "input" when e is HTMLInputElement { Type: "checkbox" } => role is "checkbox" or "button" or "menuitemcheckbox" or "option" or "switch",
            "input" when e is HTMLInputElement { Type: "radio" } => role is "radio" or "menuitemradio",
            "input" when e is HTMLInputElement { Type: "range" } => role == "slider",
            "input" when e is HTMLInputElement { Type: "number" } => role == "spinbutton",
            "input" when e is HTMLInputElement { Type: "text" or "search" or "email" or "url" } => role is "textbox" or "searchbox" or "combobox" or "spinbutton",
            "html" => role == "document",
            "br" or "head" or "link" or "meta" or "script" or "style" or "title" => false,
            _ => true
        };
    }
    public static string Implicit(Element e) => e.LocalName switch
    {
        "html" => "document", "a" or "area" when e.HasAttribute("href") => "link",
        "button" or "summary" => "button",
        "input" when e is HTMLInputElement input => input.Type switch
        {
            "checkbox" => "checkbox", "radio" => "radio", "range" => "slider", "number" => "spinbutton",
            "button" or "submit" or "reset" => "button", "search" => e.HasAttribute("list") ? "combobox" : "searchbox",
            "hidden" => "none", _ => e.HasAttribute("list") ? "combobox" : "textbox"
        },
        "textarea" => "textbox", "select" when e.HasAttribute("multiple") || Integer(e.GetAttribute("size")) > 1 => "listbox",
        "select" => "combobox", "option" => "option", "optgroup" or "fieldset" or "details" => "group",
        "img" when e.GetAttribute("alt") == "" && !e.HasAttribute("aria-label") && !e.HasAttribute("aria-labelledby") && !e.HasAttribute("tabindex") => "none",
        "img" or "svg" => "img", "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
        "ul" or "ol" or "menu" => "list", "li" => "listitem", "dl" => "descriptionlist", "dt" => "term", "dd" => "definition",
        "table" => "table", "tr" => "row", "td" => "cell", "th" when e.GetAttribute("scope") is "row" or "rowgroup" => "rowheader",
        "th" => "columnheader", "thead" or "tbody" or "tfoot" => "rowgroup", "caption" => "caption",
        "progress" => "progressbar", "meter" => "meter", "dialog" => "dialog", "hr" => "separator",
        "article" => "article", "aside" => "complementary", "main" => "main", "nav" => "navigation", "search" => "search",
        "header" when !InSection(e) => "banner", "footer" when !InSection(e) => "contentinfo",
        "section" when HasName(e) => "region", "form" when HasName(e) => "form",
        "figure" => "figure", "figcaption" => "caption", "p" => "paragraph", "blockquote" => "blockquote", "code" => "code",
        "em" => "emphasis", "strong" => "strong", "del" => "deletion", "ins" => "insertion", "sub" => "subscript", "sup" => "superscript",
        "time" => "time", "output" => "status", "math" => "math", _ => "generic"
    };
    private static bool HasName(Element e) => !string.IsNullOrWhiteSpace(e.GetAttribute("aria-label")) || e.HasAttribute("aria-labelledby") || !string.IsNullOrWhiteSpace(e.GetAttribute("title"));
    private static bool InSection(Element e)
    { for (var n = e.ParentElement; n != null; n = n.ParentElement) if (n.LocalName is "article" or "aside" or "main" or "nav" or "section") return true; return false; }
    internal static int? Integer(string? value) => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result) ? result : null;
    internal static IEnumerable<string> Tokens(string? value)
    {
        if (value == null) yield break;
        int start = 0;
        while (start < value.Length)
        {
            while (start < value.Length && IsSpace(value[start])) start++;
            int end = start;
            while (end < value.Length && !IsSpace(value[end])) end++;
            if (end > start) yield return value.Substring(start, end - start);
            start = end;
        }
    }
    private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f';
}
