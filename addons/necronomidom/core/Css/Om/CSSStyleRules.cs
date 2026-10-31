using NecronomiDom.Css.Media;
using NecronomiDom.Css.Selectors;

namespace NecronomiDom.Css.Om
{
    /// <summary>CSSOM § 6.4.3 style rule, including CSS Nesting 1 child rules.</summary>
    public sealed class CSSStyleRule : CSSGroupingRule
    {
        public CSSStyleRule(SelectorList selectors) { Selectors = selectors; Style = new CSSStyleDeclaration(this) { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public override int Type => 1;
        public SelectorList Selectors { get; private set; }
        public CSSStyleDeclaration Style { get; }
        public string SelectorText
        {
            get => Selectors.Serialize();
            set
            {
                var nested = CSSRuleParser.InStyle(ParentRule);
                var selectors = SelectorParser.ParseSelectorList(value, new SelectorParseOptions(Relative: nested, Namespaces: ParentStyleSheet?.Namespaces));
                if (selectors != null) { Selectors = nested ? SelectorParser.AbsolutizeNestedSelectors(selectors) : selectors; ParentStyleSheet?.NotifyChanged(); }
            }
        }
        protected override string SerializeRule() => CssRules.Length == 0 ? Block(SelectorText, Style.CssText) : GroupText(SelectorText, Style.CssText);
    }
    /// <summary>CSS Nesting 1 § 5 nested declaration run following a nested rule.</summary>
    public sealed class CSSNestedDeclarations : CSSRule
    {
        public CSSNestedDeclarations() { Style = new CSSStyleDeclaration(this) { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public CSSStyleDeclaration Style { get; }
        public override int Type => 0;
        protected override string SerializeRule() => Style.CssText;
    }
    /// <summary>CSSOM § 6.4.6 @media rule.</summary>
    public sealed class CSSMediaRule : CSSGroupingRule
    {
        public CSSMediaRule(MediaQueryList queries) { Media = new MediaList(queries) { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public MediaList Media { get; }
        public string ConditionText { get => Media.MediaText; set => Media.MediaText = value; }
        public override int Type => 4;
        protected override string SerializeRule() => GroupText("@media " + ConditionText);
    }
    /// <summary>CSS Conditional 3 § 3 @supports rule.</summary>
    public sealed class CSSSupportsRule : CSSGroupingRule
    {
        public CSSSupportsRule(QueryCondition condition) => Condition = condition;
        public QueryCondition Condition { get; private set; }
        public string ConditionText { get => Condition.Serialize(); set { var condition = SupportsParser.Parse(value, new SelectorParseOptions(Namespaces: ParentStyleSheet?.Namespaces)); if (condition != null) { Condition = condition; ParentStyleSheet?.NotifyChanged(); } } }
        public override int Type => 12;
        protected override string SerializeRule() => GroupText("@supports " + ConditionText);
    }
    /// <summary>CSS Conditional 5 @container rule.</summary>
    public sealed class CSSContainerRule : CSSGroupingRule
    {
        public CSSContainerRule(ContainerQuery query) => Query = query;
        public ContainerQuery Query { get; private set; }
        public string ContainerName => Query.Name ?? "";
        public QueryCondition Condition => Query.Condition;
        public string ConditionText => Query.Condition.Serialize();
        public override int Type => 0;
        protected override string SerializeRule() => GroupText("@container " + Query.Serialize());
    }
    /// <summary>CSS Transitions 2 § 3.3 @starting-style grouping rule.</summary>
    public sealed class CSSStartingStyleRule : CSSGroupingRule
    { public override int Type => 0; protected override string SerializeRule() => GroupText("@starting-style"); }
}
