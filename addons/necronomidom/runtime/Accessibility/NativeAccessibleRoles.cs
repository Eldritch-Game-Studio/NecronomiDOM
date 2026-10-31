using Godot;
using NecronomiDom.Accessibility;
using Role = Godot.AccessibilityServer.AccessibilityRole;

namespace NecronomiDom.Runtime;

/// <summary>Fixed HTML/ARIA → public Godot role table. Missing semantics are retained in role_description/live.</summary>
public static class NativeAccessibleRoles
{
    public static Role Map(string role) => role switch
    {
        "RootWebArea" or "document" => Role.Panel,
        "StaticText" or "heading" => Role.StaticText,
        // Name-prohibited semantic containers must not become Label: Label derives its name from descendant text runs.
        "paragraph" or "code" or "emphasis" or "strong" or "term" or "definition" or "caption" or "time"
            or "subscript" or "superscript" or "deletion" or "insertion" or "blockquote" => Role.Panel,
        "button" => Role.Button, "link" => Role.Link, "checkbox" => Role.CheckBox, "switch" => Role.CheckButton,
        "radio" => Role.RadioButton, "scrollbar" => Role.ScrollBar, "slider" => Role.Slider, "spinbutton" => Role.SpinButton,
        "progressbar" or "meter" => Role.ProgressIndicator, "textbox" or "searchbox" => Role.TextField,
        "table" or "grid" or "treegrid" => Role.Table, "cell" or "gridcell" => Role.Cell, "row" => Role.Row,
        "rowgroup" => Role.RowGroup, "rowheader" => Role.RowHeader, "columnheader" => Role.ColumnHeader,
        "tree" => Role.Tree, "treeitem" => Role.TreeItem, "list" or "directory" or "descriptionlist" => Role.List,
        "listitem" => Role.ListItem, "listbox" or "combobox" => Role.ListBox, "option" => Role.ListBoxOption,
        "tablist" => Role.TabBar, "tab" => Role.Tab, "tabpanel" => Role.TabPanel,
        "menubar" => Role.MenuBar, "menu" => Role.Menu, "menuitem" => Role.MenuItem,
        "menuitemcheckbox" => Role.MenuItemCheckBox, "menuitemradio" => Role.MenuItemRadio, "img" => Role.Image,
        "dialog" or "alertdialog" => Role.Dialog, "tooltip" => Role.Tooltip,
        "region" or "main" or "navigation" or "banner" or "contentinfo" or "complementary" or "search" or "form" => Role.Region,
        "none" or "presentation" => Role.Container,
        // Public Container is AccessKit IGNORED, so it cannot represent an exposed generic relation target.
        "generic" => Role.Panel,
        "group" or "radiogroup" or "toolbar" or "article" or "figure" or "feed" or "application" or "alert" or "status" or "log"
            or "timer" or "marquee" or "note" or "math" or "separator" => Role.Panel,
        _ => Role.Unknown
    };
    public static string Description(AccessibleNode node)
    {
        if (node.RoleDescription.Length != 0) return node.RoleDescription;
        return node.Role switch
        {
            "heading" => "heading" + (node.Level is { } level ? " level " + level : ""),
            "RootWebArea" or "document" => "document",
            "paragraph" or "StaticText" or "button" or "link" or "checkbox" or "radio" or "scrollbar" or "slider" or "spinbutton"
                or "progressbar" or "textbox" or "table" or "cell" or "row" or "rowgroup" or "rowheader" or "columnheader"
                or "tree" or "treeitem" or "list" or "listitem" or "listbox" or "option" or "tab" or "tabpanel" or "menubar"
                or "menu" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "img" or "dialog" or "tooltip" or "region" => "",
            _ => node.Role
        };
    }
}
