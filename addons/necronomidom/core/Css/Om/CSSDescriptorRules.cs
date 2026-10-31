namespace NecronomiDom.Css.Om
{
    /// <summary>CSSOM § 6.4.7 @font-face descriptor block.</summary>
    public sealed class CSSFontFaceRule : CSSRule
    {
        public CSSFontFaceRule() { Style = new CSSStyleDeclaration(this, "font-face") { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public CSSStyleDeclaration Style { get; }
        public override int Type => 5;
        protected override string SerializeRule() => Block("@font-face", Style.CssText);
    }
    /// <summary>CSS Counter Styles 3 § 3 @counter-style descriptors.</summary>
    public sealed partial class CSSCounterStyleRule : CSSRule
    {
        public CSSCounterStyleRule(string name) { _name = CanonicalName(name); Style = new CSSStyleDeclaration(this, "counter-style") { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        private string _name;
        public string Name { get => Syntax.CssSerialization.SerializeIdentifier(_name); set { if (!NonOverridable(value)) { _name = CanonicalName(value); ParentStyleSheet?.NotifyChanged(); } } }
        public CSSStyleDeclaration Style { get; }
        public string System { get => Style.GetPropertyValue("system"); set => SetDescriptor("system", value); }
        public string Symbols { get => Style.GetPropertyValue("symbols"); set => SetDescriptor("symbols", value); }
        public string AdditiveSymbols { get => Style.GetPropertyValue("additive-symbols"); set => SetDescriptor("additive-symbols", value); }
        public string Negative { get => Style.GetPropertyValue("negative"); set => SetDescriptor("negative", value); }
        public string Prefix { get => Style.GetPropertyValue("prefix"); set => SetDescriptor("prefix", value); }
        public string Suffix { get => Style.GetPropertyValue("suffix"); set => SetDescriptor("suffix", value); }
        public string Range { get => Style.GetPropertyValue("range"); set => SetDescriptor("range", value); }
        public string Pad { get => Style.GetPropertyValue("pad"); set => SetDescriptor("pad", value); }
        public string SpeakAs { get => Style.GetPropertyValue("speak-as"); set => SetDescriptor("speak-as", value); }
        public string Fallback { get => Style.GetPropertyValue("fallback"); set => SetDescriptor("fallback", value); }
        public override int Type => 11;
        protected override string SerializeRule() => Block("@counter-style " + Name, Style.CssText);
    }
    /// <summary>CSS Properties and Values API 1 § 3 @property registration rule.</summary>
    public sealed class CSSPropertyRule : CSSRule
    {
        public CSSPropertyRule(string name) { Name = name; Style = new CSSStyleDeclaration(this, "property") { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public string Name { get; }
        public CSSStyleDeclaration Style { get; }
        public string Syntax => Style.GetSpecifiedValue("syntax") is NecronomiDom.Css.Values.CssStringValue value ? value.Value : "";
        public bool Inherits => Style.GetPropertyValue("inherits") == "true";
        public string InitialValue => Style.GetPropertyValue("initial-value");
        public override int Type => 0;
        protected override string SerializeRule() => Block("@property " + SyntaxSerialization(Name), Style.CssText);
        private static string SyntaxSerialization(string name) => NecronomiDom.Css.Syntax.CssSerialization.SerializeIdentifier(name);
    }
    /// <summary>CSS Anchor Positioning 1 @position-try descriptor rule.</summary>
    public sealed class CSSPositionTryRule : CSSRule
    {
        public CSSPositionTryRule(string name) { Name = name; Style = new CSSStyleDeclaration(this, "position-try") { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public string Name { get; }
        public CSSStyleDeclaration Style { get; }
        public override int Type => 0;
        protected override string SerializeRule() => Block("@position-try " + Syntax.CssSerialization.SerializeIdentifier(Name), Style.CssText);
    }
}
