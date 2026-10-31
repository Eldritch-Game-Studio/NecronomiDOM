using System;
using System.Collections.Generic;
using System.Globalization;

namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4.10.5 input value/checked dirty state and constraint validation.</summary>
    public sealed partial class HTMLInputElement : HTMLElement
    {
        private string? _value;
        private bool? _checked;
        private string _customValidity = "";
        private string _type = "text";
        private static readonly string[] InputTypes = { "password", "search", "tel", "number", "range", "checkbox", "radio", "hidden", "button", "submit", "reset", "email", "url" };
        internal bool UserEdited { get; private set; }
        internal bool UserBadInput { get; private set; }
        internal HTMLInputElement(Document doc) : base(doc, "input") { }
        public string Type { get => _type; set => SetAttribute("type", value); }
        protected internal override void AttributeChanged(string localName, string? ns, string? oldValue, string? newValue)
        {
            base.AttributeChanged(localName, ns, oldValue, newValue);
            if (ns != null || localName != "type") return;
            _type = "text";
            foreach (var type in InputTypes) if (Names.AsciiEqual(newValue, type)) { _type = type; break; }
        }
        public string Name { get => GetAttribute("name") ?? ""; set => SetAttribute("name", value); }
        public bool Disabled { get => HasAttribute("disabled"); set => ToggleAttribute("disabled", value); }
        public bool Required { get => HasAttribute("required"); set => ToggleAttribute("required", value); }
        public bool ReadOnly { get => HasAttribute("readonly"); set => ToggleAttribute("readonly", value); }
        public string DefaultValue { get => GetAttribute("value") ?? ""; set => SetAttribute("value", value); }
        public bool DefaultChecked { get => HasAttribute("checked"); set => ToggleAttribute("checked", value); }
        public string Value
        {
            get => Type is "checkbox" or "radio" ? GetAttribute("value") ?? "on" : Sanitize(_value ?? DefaultValue);
            set { var old = Value; UserEdited = false; UserBadInput = false; if (Type is "checkbox" or "radio" or "hidden" or "button" or "submit" or "reset") SetAttribute("value", value); else _value = Sanitize(value); if (IsTextEditable && Value != old) Editing.ValueChanged(Value); NotifyStateChanged(); }
        }
        /// <summary>Input-adapter entry point; distinguishes user edits from Web IDL programmatic value writes.</summary>
        public void SetUserValue(string value)
        {
            Value = value; UserEdited = true;
            UserBadInput = Type == "number" && value.Length > 0 && !FormValidation.Number(value, out _);
            NotifyStateChanged();
        }
        private string Sanitize(string value)
        {
            if (Type == "number") return FormValidation.Number(value, out _) ? value : "";
            if (Type == "range")
            {
                var min = FormValidation.Number(GetAttribute("min"), out var m) ? m : 0;
                var max = FormValidation.Number(GetAttribute("max"), out var x) ? x : 100;
                var n = FormValidation.Number(value, out var v) ? v : max < min ? min : min / 2 + max / 2;
                n = Math.Max(min, Math.Min(Math.Max(min, max), n));
                var step = GetAttribute("step");
                if (!Names.AsciiEqual(step, "any"))
                {
                    var s = FormValidation.Number(step, out var parsed) && parsed > 0 ? parsed : 1;
                    var basis = FormValidation.Number(GetAttribute("min"), out var baseMin) ? baseMin : FormValidation.Number(DefaultValue, out var initial) ? initial : 0;
                    var low = Math.Ceiling((min - basis) / s);
                    var high = Math.Floor((Math.Max(min, max) - basis) / s);
                    if (high >= low) n = basis + Math.Clamp(Math.Floor((n - basis) / s + 0.5), low, high) * s;
                }
                return n.ToString("G", CultureInfo.InvariantCulture);
            }
            value = value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
            return Type is "url" or "email" ? value.Trim(' ', '\t', '\f', '\r', '\n') : value;
        }
        public bool Checked
        {
            get => _checked ?? DefaultChecked;
            set
            {
                _checked = value;
                if (value && Type == "radio" && Name.Length > 0)
                    foreach (var n in Root.InclusiveDescendants()) if (n is HTMLInputElement radio && !ReferenceEquals(this, radio) && radio.Type == "radio" && radio.Name == Name && ReferenceEquals(FormValidation.FormOwner(radio), FormValidation.FormOwner(this))) { radio._checked = false; radio.NotifyStateChanged(); }
                NotifyStateChanged();
            }
        }
        private bool _indeterminate;
        public bool Indeterminate { get => _indeterminate; set { _indeterminate = value; NotifyStateChanged(); } }
        public bool WillValidate => !FormValidation.Disabled(this) && !ReadOnly && Type is not ("hidden" or "button" or "reset");
        public ValidityState Validity => FormValidation.Input(this, _customValidity);
        public void SetCustomValidity(string message) { _customValidity = message; NotifyStateChanged(); }
        public string ValidationMessage => WillValidate && !Validity.Valid ? _customValidity.Length > 0 ? _customValidity : "The value does not satisfy the control constraints." : "";
        public bool CheckValidity() => FormValidation.Check(this, WillValidate, Validity);
        public bool ReportValidity() => CheckValidity();
    }
    /// <summary>HTML § 4.10.11 textarea dirty value and constraint validation.</summary>
    public sealed partial class HTMLTextAreaElement : HTMLElement
    {
        private string? _value;
        private string _customValidity = "";
        internal bool UserEdited { get; private set; }
        internal HTMLTextAreaElement(Document doc) : base(doc, "textarea") { }
        /// <summary>HTML § 4.10.11 name content-attribute reflection.</summary>
        public string Name { get => GetAttribute("name") ?? ""; set => SetAttribute("name", value); }
        public string DefaultValue { get => TextContent ?? ""; set => TextContent = value; }
        public string Value { get => Normalize(_value ?? DefaultValue); set { var old = Value; _value = Normalize(value); UserEdited = false; if (Value != old) Editing.ValueChanged(Value); NotifyStateChanged(); } }
        /// <summary>Input-adapter entry point for user length-constraint state.</summary>
        public void SetUserValue(string value) { Value = value; UserEdited = true; NotifyStateChanged(); }
        private static string Normalize(string v) => v.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        public bool Disabled { get => HasAttribute("disabled"); set => ToggleAttribute("disabled", value); }
        public bool Required { get => HasAttribute("required"); set => ToggleAttribute("required", value); }
        public bool ReadOnly { get => HasAttribute("readonly"); set => ToggleAttribute("readonly", value); }
        public bool WillValidate => !FormValidation.Disabled(this) && !ReadOnly;
        public ValidityState Validity => FormValidation.Text(this, Value, _customValidity);
        public void SetCustomValidity(string message) { _customValidity = message; NotifyStateChanged(); }
        public bool CheckValidity() => FormValidation.Check(this, WillValidate, Validity);
        public bool ReportValidity() => CheckValidity();
    }
    /// <summary>HTML § 4.10.7 select value, options and constraint validation.</summary>
    public sealed partial class HTMLSelectElement : HTMLElement
    {
        private string _customValidity = "";
        private HTMLCollection? _options;
        internal HTMLSelectElement(Document doc) : base(doc, "select") { }
        public HTMLCollection Options => _options ??= new(this, e => e is HTMLOptionElement option && ReferenceEquals(option.SelectOwner, this));
        public bool Multiple { get => HasAttribute("multiple"); set => ToggleAttribute("multiple", value); }
        public bool Disabled { get => HasAttribute("disabled"); set => ToggleAttribute("disabled", value); }
        public bool Required { get => HasAttribute("required"); set => ToggleAttribute("required", value); }
        internal HTMLOptionElement? DefaultSelected
        {
            get
            {
                HTMLOptionElement? first = null; HTMLOptionElement? last = null; var dirty = false;
                for (var node = FirstChild; node != null; node = SelectedContentProjection.Next(this, node))
                    if (node is HTMLOptionElement o && ReferenceEquals(o.SelectOwner, this))
                    {
                        if (!o.Disabled && !(o.ParentElement is HTMLOptGroupElement group && group.HasAttribute("disabled"))) first ??= o;
                        if (o.DefaultSelected) last = o;
                        if (o.DirtySelected != null) dirty = true;
                    }
                return dirty || Multiple ? null : last ?? first;
            }
        }
        public string Value
        {
            get { foreach (var e in Options) if (e is HTMLOptionElement { Selected: true } option) return option.Value; return ""; }
            set { var selected = false; foreach (var e in Options) if (e is HTMLOptionElement option) { var match = !selected && option.Value == value; option.DirtySelected = match; selected |= match; } }
        }
        public int SelectedIndex
        {
            get { var i = 0; foreach (var e in Options) { if (((HTMLOptionElement)e).Selected) return i; i++; } return -1; }
            set { var i = 0; foreach (var e in Options) ((HTMLOptionElement)e).DirtySelected = i++ == value; }
        }
        public bool WillValidate => !FormValidation.Disabled(this);
        public ValidityState Validity => FormValidation.Select(this, _customValidity);
        public void SetCustomValidity(string message) { _customValidity = message; NotifyStateChanged(); }
        public bool CheckValidity() => FormValidation.Check(this, WillValidate, Validity);
        public bool ReportValidity() => CheckValidity();
    }
    /// <summary>HTML § 4.10.10 option selectedness and value reflection.</summary>
    public sealed class HTMLOptionElement : HTMLElement
    {
        private bool? _dirtySelected;
        internal bool? DirtySelected
        {
            get => _dirtySelected;
            set { _dirtySelected = value; NotifyStateChanged(); SelectOwner?.NotifyStateChanged(); }
        }
        internal HTMLOptionElement(Document doc) : base(doc, "option") { }
        public bool Disabled { get => HasAttribute("disabled"); set => ToggleAttribute("disabled", value); }
        public bool DefaultSelected { get => HasAttribute("selected"); set => ToggleAttribute("selected", value); }
        internal HTMLSelectElement? SelectOwner { get { for (var n = ParentElement; n != null; n = n.ParentElement) if (n is HTMLSelectElement s) return s; return null; } }
        public bool Selected
        {
            get => DirtySelected ?? (SelectOwner is { Multiple: false } owner ? ReferenceEquals(owner.DefaultSelected, this) : DefaultSelected);
            set { DirtySelected = value; if (value && SelectOwner is { Multiple: false } owner) foreach (var e in owner.Options) if (!ReferenceEquals(e, this)) ((HTMLOptionElement)e).DirtySelected = false; }
        }
        public string Text { get => HtmlText.CollapseWhitespace(TextContent ?? ""); set => TextContent = value; }
        public string Value { get => GetAttribute("value") ?? Text; set => SetAttribute("value", value); }
    }
}
