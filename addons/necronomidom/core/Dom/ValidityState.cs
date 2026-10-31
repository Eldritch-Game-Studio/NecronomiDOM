using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4.10.21 ValidityState constraint flags.</summary>
    public sealed class ValidityState
    {
        public bool ValueMissing { get; internal set; }
        public bool TypeMismatch { get; internal set; }
        public bool PatternMismatch { get; internal set; }
        public bool TooLong { get; internal set; }
        public bool TooShort { get; internal set; }
        public bool RangeUnderflow { get; internal set; }
        public bool RangeOverflow { get; internal set; }
        public bool StepMismatch { get; internal set; }
        public bool BadInput { get; internal set; }
        public bool CustomError { get; internal set; }
        public bool Valid => !(ValueMissing || TypeMismatch || PatternMismatch || TooLong || TooShort || RangeUnderflow || RangeOverflow || StepMismatch || BadInput || CustomError);
    }
    internal static class FormValidation
    {
        internal static bool Number(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text) || text[0] == '+' || text != text.Trim() || text.EndsWith(".", StringComparison.Ordinal)) return false;
            return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }
        internal static Element? FormOwner(Element e)
        {
            if (e.GetAttribute("form") is { } id) return e.NodeDocument.GetElementById(id) is HTMLFormElement form ? form : null;
            for (var n = e.ParentElement; n != null; n = n.ParentElement) if (n is HTMLFormElement) return n;
            return null;
        }
        internal static bool Disabled(Element e)
        {
            if (e.HasAttribute("disabled")) return true;
            for (var parent = e.ParentElement; parent != null; parent = parent.ParentElement)
            {
                if (parent is not HTMLFieldSetElement || !parent.HasAttribute("disabled")) continue;
                Element? legend = null; foreach (var n in parent.Children) if (n is HTMLLegendElement) { legend = n; break; }
                if (legend == null || !legend.Contains(e)) return true;
            }
            return false;
        }
        internal static ValidityState Text(Element e, string value, string custom)
        {
            var state = new ValidityState { ValueMissing = e.HasAttribute("required") && !e.HasAttribute("readonly") && value.Length == 0, CustomError = custom.Length > 0 };
            var userEdited = e is HTMLTextAreaElement { UserEdited: true } ||
                e is HTMLInputElement { UserEdited: true } input && input.Type is "text" or "password" or "search" or "email" or "url";
            if (userEdited && value.Length > 0)
            {
                state.TooShort = int.TryParse(e.GetAttribute("minlength"), out var min) && min >= 0 && value.Length < min;
                state.TooLong = int.TryParse(e.GetAttribute("maxlength"), out var max) && max >= 0 && value.Length > max;
            }
            return state;
        }
        internal static ValidityState Input(HTMLInputElement e, string custom)
        {
            var value = e.Value; var result = Text(e, value, custom);
            result.BadInput = e.UserBadInput;
            if (e.Type == "checkbox") result.ValueMissing = e.Required && !e.Checked;
            else if (e.Type == "radio")
            {
                var required = false; var selected = false;
                foreach (var n in e.Root.InclusiveDescendants())
                    if (n is HTMLInputElement radio && radio.Type == "radio" && (ReferenceEquals(e, radio) || e.Name.Length > 0 && radio.Name == e.Name) && ReferenceEquals(FormOwner(e), FormOwner(radio))) { required |= radio.Required; selected |= radio.Checked; }
                result.ValueMissing = required && !selected;
            }
            else if (e.Type == "range") result.ValueMissing = false;
            if (value.Length == 0) return result;
            if (e.Type is "text" or "password" or "search" or "email" or "url")
            {
                if (e.GetAttribute("pattern") is { } pattern)
                {
                    try { result.PatternMismatch = !Regex.IsMatch(value, "\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
                    catch (ArgumentException) { result.PatternMismatch = false; }
                }
                if (e.Type == "email") result.TypeMismatch = !Regex.IsMatch(value, "^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)*$");
                if (e.Type == "url") result.TypeMismatch = Url.WebUrl.Parse(value) == null;
            }
            if (e.Type is "number" or "range" && Number(value, out var number))
            {
                var hasMin = Number(e.GetAttribute("min"), out var min); var hasMax = Number(e.GetAttribute("max"), out var max);
                result.RangeUnderflow = hasMin && number < min; result.RangeOverflow = hasMax && number > max;
                if (!Names.AsciiEqual(e.GetAttribute("step"), "any"))
                {
                    var step = Number(e.GetAttribute("step"), out var parsed) && parsed > 0 ? parsed : 1;
                    var basis = hasMin ? min : Number(e.DefaultValue, out var initial) ? initial : 0;
                    var ratio = (number - basis) / step;
                    result.StepMismatch = Math.Abs(ratio - Math.Round(ratio)) > 1e-7;
                }
            }
            return result;
        }
        internal static ValidityState Select(HTMLSelectElement e, string custom)
        {
            var selected = false; var placeholder = false; var index = 0;
            var displaySize = int.TryParse(e.GetAttribute("size"), out var size) && size > 0 ? size : 1;
            foreach (var element in e.Options)
            {
                var option = (HTMLOptionElement)element;
                if (option.Selected) { selected = true; placeholder |= !e.Multiple && displaySize == 1 && index == 0 && option.Value.Length == 0 && ReferenceEquals(option.ParentElement, e); }
                index++;
            }
            return new ValidityState { ValueMissing = e.Required && (!selected || placeholder), CustomError = custom.Length > 0 };
        }
        internal static bool Check(Element e, bool willValidate, ValidityState state)
        { if (!willValidate || state.Valid) return true; e.DispatchEvent(new Event("invalid", new EventInit { Cancelable = true })); return false; }
    }
}
