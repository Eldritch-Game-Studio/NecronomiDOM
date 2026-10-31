using System;
using System.Globalization;
using NecronomiDom.Dom;

namespace NecronomiDom.Accessibility;

public sealed partial class AccessibleTree
{
    private AccessibleLive Live(Element e)
    {
        for (Node? n = e; n != null; n = n.ParentNode ?? (n is ShadowRoot sr ? sr.Host : null))
            if (n is Element ancestor)
            {
                Depend(n);
                string? live = ancestor.GetAttribute("aria-live");
                if (live is "assertive" or "polite" or "off") return live == "assertive" ? AccessibleLive.Assertive : live == "polite" ? AccessibleLive.Polite : AccessibleLive.Off;
                string role = AccessibleRoles.Compute(ancestor, Focusable(ancestor));
                if (role == "alert") return AccessibleLive.Assertive;
                if (role is "status" or "log") return AccessibleLive.Polite;
            }
        return AccessibleLive.Off;
    }
    private (bool Atomic, string Relevant) LiveMetadata(Element e)
    {
        bool? atomic = null; string? relevant = null;
        for (Node? n = e; n != null; n = n.ParentNode ?? (n is ShadowRoot sr ? sr.Host : null))
            if (n is Element ancestor)
            {
                Depend(n);
                atomic ??= Boolean(ancestor.GetAttribute("aria-atomic"));
                if (atomic == null && AccessibleRoles.Compute(ancestor, Focusable(ancestor)) is "alert" or "status") atomic = true;
                if (relevant == null && ancestor.GetAttribute("aria-relevant") is { } tokens)
                {
                    int flags = 0;
                    foreach (string token in AccessibleRoles.Tokens(tokens))
                        flags |= token switch { "all" => 7, "additions" => 1, "removals" => 2, "text" => 4, _ => 0 };
                    relevant = flags switch
                    {
                        1 => "additions", 2 => "removals", 3 => "additions removals", 4 => "text",
                        6 => "removals text", 7 => "additions removals text", _ => "additions text"
                    };
                }
            }
        return (atomic ?? false, relevant ?? "additions text");
    }
    private AccessibleStates States(Element e, string role)
    {
        bool form = IsLabelable(e);
        // Native disabledness depends on fieldset/legend ancestors even when aria-disabled short-circuits.
        for (var ancestor = e.ParentElement; ancestor != null; ancestor = ancestor.ParentElement) Depend(ancestor);
        bool disabled = form && FormValidation.Disabled(e) || InheritedTrue(e, "aria-disabled");
        bool readOnly = (e is HTMLInputElement or HTMLTextAreaElement && e.HasAttribute("readonly"))
            || role is "checkbox" or "combobox" or "grid" or "gridcell" or "listbox" or "radiogroup" or "slider" or "spinbutton" or "textbox" && e.GetAttribute("aria-readonly") == "true";
        bool required = (e is HTMLInputElement or HTMLSelectElement or HTMLTextAreaElement && e.HasAttribute("required"))
            || role is "checkbox" or "combobox" or "gridcell" or "listbox" or "radiogroup" or "spinbutton" or "textbox" or "tree" && e.GetAttribute("aria-required") == "true";
        AccessibleChecked check = TokenChecked(e.GetAttribute("aria-checked"));
        if (e is HTMLInputElement input && input.Type is "checkbox" or "radio") check = input.Type == "checkbox" && input.Indeterminate ? AccessibleChecked.Mixed : input.Checked ? AccessibleChecked.True : AccessibleChecked.False;
        if (role is not ("checkbox" or "radio" or "switch" or "menuitemcheckbox" or "menuitemradio" or "option")) check = AccessibleChecked.Undefined;
        if (role is "radio" or "switch" or "menuitemradio" && check == AccessibleChecked.Mixed) check = AccessibleChecked.False;
        bool? selected = e is HTMLOptionElement option ? option.Selected
            : role is "gridcell" or "option" or "row" or "tab" or "treeitem" or "columnheader" or "rowheader" ? Boolean(e.GetAttribute("aria-selected")) : null;
        bool? expanded = role is "application" or "button" or "checkbox" or "columnheader" or "combobox" or "gridcell" or "link" or "listbox" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "row" or "rowheader" or "switch" or "tab" or "treeitem"
            ? Boolean(e.GetAttribute("aria-expanded")) : null;
        if (e.LocalName == "summary" && e.ParentElement is { LocalName: "details" } details) { Depend(details); expanded = details.HasAttribute("open"); }
        if (e is HTMLSelectElement && role == "combobox") expanded ??= false;
        string value = e.GetAttribute("aria-valuetext") ?? (e switch { HTMLInputElement i when i.Type is not ("checkbox" or "radio" or "button" or "submit" or "reset") => i.Value, HTMLTextAreaElement t => t.Value, HTMLSelectElement s => s.Value, _ => role == "textbox" ? e.TextContent ?? "" : "" });
        double? now = Number(e.GetAttribute("aria-valuenow")), min = Number(e.GetAttribute("aria-valuemin")), max = Number(e.GetAttribute("aria-valuemax"));
        if (e is HTMLInputElement { Type: "range" or "number" } number)
        { now = Number(number.Value); min = Number(e.GetAttribute("min")) ?? (number.Type == "range" ? 0 : null); max = Number(e.GetAttribute("max")) ?? (number.Type == "range" ? 100 : null); }
        if (e.LocalName is "progress" or "meter") { now = Number(e.GetAttribute("value")) ?? 0; min = Number(e.GetAttribute("min")) ?? 0; max = Number(e.GetAttribute("max")) ?? 1; }
        string invalid = e.GetAttribute("aria-invalid") ?? "false";
        if (invalid is not ("false" or "grammar" or "spelling")) invalid = "true";
        if (e is HTMLInputElement { UserEdited: true, WillValidate: true } edited && !edited.Validity.Valid
            || e is HTMLTextAreaElement { UserEdited: true, WillValidate: true } editedArea && !editedArea.Validity.Valid) invalid = "true";
        string popup = e.GetAttribute("aria-haspopup") ?? (role == "combobox" ? "listbox" : "false");
        if (popup == "true") popup = "menu";
        if (popup is not ("menu" or "listbox" or "tree" or "grid" or "dialog")) popup = "false";
        string orientation = e.GetAttribute("aria-orientation") ?? (role is "slider" or "separator" or "tablist" or "toolbar" ? "horizontal" : role is "scrollbar" or "listbox" or "menu" or "tree" ? "vertical" : "");
        return new(disabled, readOnly, required, e.GetAttribute("aria-busy") == "true", role is "dialog" or "alertdialog" && e.GetAttribute("aria-modal") == "true",
            e is HTMLSelectElement { Multiple: true } || role is "grid" or "listbox" or "tablist" or "tree" or "treegrid" && e.GetAttribute("aria-multiselectable") == "true", Focusable(e), ReferenceEquals(e, _document.FocusedElement),
            check, role == "button" ? TokenChecked(e.GetAttribute("aria-pressed")) : AccessibleChecked.Undefined, selected, expanded, invalid, popup,
            value, now, min, max, orientation, AccessibleRoles.Integer(e.GetAttribute("aria-posinset")), AccessibleRoles.Integer(e.GetAttribute("aria-setsize")));
    }
    private bool InheritedTrue(Element e, string attr)
    { for (Node? n = e; n != null; n = n.ParentNode) if (n is Element ancestor) { Depend(n); if (ancestor.GetAttribute(attr) == "true") return true; } return false; }
    private static AccessibleChecked TokenChecked(string? value) => value switch { "true" => AccessibleChecked.True, "false" => AccessibleChecked.False, "mixed" => AccessibleChecked.Mixed, _ => AccessibleChecked.Undefined };
    private static bool? Boolean(string? value) => value == "true" ? true : value == "false" ? false : null;
    private static double? Number(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && double.IsFinite(result) ? result : null;
}
