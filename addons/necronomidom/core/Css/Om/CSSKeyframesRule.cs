using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Om
{
    /// <summary>CSS Animations 1 § 5 CSSKeyframesRule mutation and keyframe lookup.</summary>
    public sealed class CSSKeyframesRule : CSSGroupingRule
    {
        public CSSKeyframesRule(string name, bool quotedName = false) { _name = name; QuotedName = quotedName; }
        private string _name;
        internal bool QuotedName { get; }
        public string Name { get => _name; set { _name = value; ParentStyleSheet?.NotifyChanged(); } }
        public override int Type => 7;
        protected override string SerializeRule() => GroupText("@keyframes " + (QuotedName ? CssSerialization.SerializeString(Name) : CssSerialization.SerializeIdentifier(Name)));
        public void AppendRule(string rule)
        {
            var parsed = CSSRuleParser.ParseSingle(rule, ParentStyleSheet, this) as CSSKeyframeRule;
            if (parsed == null) return;
            parsed.Attach(ParentStyleSheet, this);
            CssRules.Add(parsed);
            ParentStyleSheet?.NotifyChanged();
        }
        public CSSKeyframeRule? FindRule(string select)
        {
            var keys = ParseKeys(CssParser.ParseComponentValueList(select));
            if (keys == null) return null;
            var text = SerializeKeys(keys);
            for (var i = CssRules.Length - 1; i >= 0; i--) if (CssRules[i] is CSSKeyframeRule frame && frame.KeyText == text) return frame;
            return null;
        }
        public void DeleteRule(string select)
        {
            var rule = FindRule(select);
            if (rule == null) return;
            for (var i = CssRules.Length - 1; i >= 0; i--) if (ReferenceEquals(CssRules[i], rule)) { base.DeleteRule(i); break; }
        }
        internal static IReadOnlyList<double>? ParseKeys(IReadOnlyList<ComponentValue> source)
        {
            var result = new List<double>();
            foreach (var entry in CssParser.SplitComma(source))
            {
                var values = CssParser.Trim(entry);
                if (values.Count != 1 || values[0] is not PreservedToken token) return null;
                if (token.Token.IsIdent("from")) result.Add(0);
                else if (token.Token.IsIdent("to")) result.Add(100);
                else if (token.Token.Kind == CssTokenKind.Percentage && token.Token.Number >= 0 && token.Token.Number <= 100) result.Add(token.Token.Number);
                else return null;
            }
            return result.Count == 0 ? null : result;
        }
        internal static string SerializeKeys(IReadOnlyList<double> keys)
        { var result = new StringBuilder(); foreach (var key in keys) { if (result.Length > 0) result.Append(", "); result.Append(CssSerialization.SerializeNumber(key)).Append('%'); } return result.ToString(); }
    }
    /// <summary>CSS Animations 1 § 5 CSSKeyframeRule; important declarations are ignored.</summary>
    public sealed class CSSKeyframeRule : CSSRule
    {
        public CSSKeyframeRule(IReadOnlyList<double> keys) { Keys = keys; Style = new CSSStyleDeclaration(this) { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public IReadOnlyList<double> Keys { get; private set; }
        public CSSStyleDeclaration Style { get; }
        public string KeyText
        {
            get => CSSKeyframesRule.SerializeKeys(Keys);
            set { var keys = CSSKeyframesRule.ParseKeys(CssParser.ParseComponentValueList(value)); if (keys == null) throw new Dom.DomException("Invalid keyframe selector.", "SyntaxError"); Keys = keys; ParentStyleSheet?.NotifyChanged(); }
        }
        public override int Type => 8;
        protected override string SerializeRule() => Block(KeyText, Style.CssText);
    }
}
