using System;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorMatcher
    {
        private static bool MatchesState(Element e, string name, SelectorMatchContext context)
        {
            if (context.State != null) return context.State(e, name);
            switch (name)
            {
                case "hover": return HasStateInTree(e, ElementPseudoState.Hover);
                case "active": return HasStateInTree(e, ElementPseudoState.Active);
                case "focus": return (e.PseudoState & ElementPseudoState.Focus) != 0;
                case "focus-visible": return (e.PseudoState & ElementPseudoState.FocusVisible) != 0;
                case "focus-within": return HasStateInTree(e, ElementPseudoState.Focus);
                case "modal": return (e.PseudoState & ElementPseudoState.Modal) != 0;
                case "popover-open": return (e.PseudoState & ElementPseudoState.PopoverOpen) != 0;
                case "enabled": return CanBeDisabled(e) && !IsDisabled(e);
                case "disabled": return CanBeDisabled(e) && IsDisabled(e);
                case "checked": return e is HTMLInputElement input && input.Type is "checkbox" or "radio" && input.Checked
                    || e is HTMLOptionElement option && option.Selected;
                case "indeterminate": return e is HTMLInputElement { Type: "checkbox", Indeterminate: true }
                    || (e.PseudoState & ElementPseudoState.Indeterminate) != 0
                    || e.LocalName == "progress" && !e.HasAttribute("value") || IsIndeterminateRadio(e);
                case "placeholder-shown": return e is HTMLInputElement placeholderInput && placeholderInput.Type is "text" or "search" or "url" or "tel" or "email" or "password" or "number"
                    && placeholderInput.HasAttribute("placeholder") && placeholderInput.Value.Length == 0
                    || e is HTMLTextAreaElement area && area.HasAttribute("placeholder") && area.Value.Length == 0;
                case "default": return e is HTMLInputElement defaultInput && defaultInput.Type is "checkbox" or "radio" && defaultInput.HasAttribute("checked")
                    || e is HTMLOptionElement defaultOption && defaultOption.HasAttribute("selected") || IsDefaultButton(e);
                case "read-write": return IsTextEditable(e) && !IsDisabled(e) && !e.HasAttribute("readonly");
                case "read-only": return !(IsTextEditable(e) && !IsDisabled(e) && !e.HasAttribute("readonly"));
                case "in-range": return HasRange(e) && !IsOutOfRange(e);
                case "out-of-range": return HasRange(e) && IsOutOfRange(e);
                case "valid": return IsValidationCandidate(e) && IsValid(e);
                case "invalid": return IsValidationCandidate(e) && !IsValid(e);
                case "link": case "any-link": return e.LocalName is "a" or "area" or "link" && e.HasAttribute("href");
                case "visited": return false;
                default: return false;
            }
        }

        private static bool HasStateInTree(Element e, ElementPseudoState state)
        {
            foreach (Node node in e.ShadowIncludingInclusiveDescendants())
                if (node is Element element && (element.PseudoState & state) != 0) return true;
            return false;
        }
        private static bool CanBeDisabled(Element e) => e.LocalName is "button" or "input" or "select" or "textarea" or "fieldset" or "optgroup" or "option";
        private static bool IsDisabled(Element e)
        {
            if (e.HasAttribute("disabled")) return true;
            if (e.LocalName == "option" && e.ParentElement is { LocalName: "optgroup" } group && group.HasAttribute("disabled")) return true;
            for (var parent = e.ParentElement; parent != null; parent = parent.ParentElement)
            {
                if (parent.LocalName != "fieldset" || !parent.HasAttribute("disabled")) continue;
                Element? legend = null;
                for (Node? node = parent.FirstChild; node != null; node = node.NextSibling)
                    if (node is Element { LocalName: "legend" } first) { legend = first; break; }
                if (legend == null || !legend.Contains(e)) return true;
            }
            return false;
        }
        private static bool IsTextEditable(Element e) => e is HTMLTextAreaElement
            || e is HTMLInputElement input && input.Type is "text" or "password" or "search" or "url" or "email" or "tel" or "number";
        private static bool HasRange(Element e) => e is HTMLInputElement input && !IsDisabled(e) && !e.HasAttribute("readonly")
            && (input.Type == "range" || input.Type == "number" && (e.HasAttribute("min") || e.HasAttribute("max")));
        private static bool IsOutOfRange(Element e) => e is HTMLInputElement input && (input.Validity.RangeOverflow || input.Validity.RangeUnderflow);
        private static bool IsValidationCandidate(Element e) => e switch
        {
            HTMLInputElement input => input.WillValidate,
            HTMLTextAreaElement area => area.WillValidate,
            HTMLSelectElement select => select.WillValidate,
            _ => e.LocalName is "form" or "fieldset",
        };
        private static bool IsValid(Element e)
        {
            if (e is HTMLInputElement input) return input.Validity.Valid;
            if (e is HTMLTextAreaElement area) return area.Validity.Valid;
            if (e is HTMLSelectElement select) return select.Validity.Valid;
            Node root = e.LocalName == "form" ? e.Root : e;
            foreach (Node node in root.InclusiveDescendants())
                if (node is Element control && !ReferenceEquals(control, e) && control.LocalName is not ("form" or "fieldset")
                    && (e.LocalName != "form" || ReferenceEquals(FormValidation.FormOwner(control), e))
                    && IsValidationCandidate(control) && !IsValid(control)) return false;
            return true;
        }
        private static bool IsIndeterminateRadio(Element e)
        {
            if (e is not HTMLInputElement { Type: "radio" } radio) return false;
            string group = radio.Name;
            foreach (Node n in radio.Root.InclusiveDescendants())
                if (n is HTMLInputElement { Type: "radio" } other && (ReferenceEquals(other, radio) || group.Length > 0 && other.Name == group)
                    && ReferenceEquals(FormValidation.FormOwner(other), FormValidation.FormOwner(radio)) && other.Checked) return false;
            return true;
        }
        private static bool IsDefaultButton(Element e)
        {
            if (e.LocalName is not ("button" or "input") || (e.GetAttribute("type") ?? (e.LocalName == "button" ? "submit" : "text")) != "submit") return false;
            Element? form = FormValidation.FormOwner(e);
            if (form == null) return false;
            foreach (Node n in form.Root.InclusiveDescendants())
                if (n is Element button && button.LocalName is "button" or "input"
                    && ReferenceEquals(FormValidation.FormOwner(button), form)
                    && (button.GetAttribute("type") ?? (button.LocalName == "button" ? "submit" : "text")) == "submit") return ReferenceEquals(button, e);
            return false;
        }
    }
}
