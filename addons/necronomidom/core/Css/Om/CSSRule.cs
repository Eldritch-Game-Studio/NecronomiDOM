using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Css.Om
{
    /// <summary>CSSOM § 6.4 CSSRule ownership and serialization interface.</summary>
    public abstract class CSSRule
    {
        public abstract int Type { get; }
        public CSSRule? ParentRule { get; internal set; }
        public CSSStyleSheet? ParentStyleSheet { get; internal set; }
        public string CssText
        {
            get => SerializeRule();
            set { _ = value; } // CSSOM § 6.4 specifies that this legacy setter does nothing.
        }
        protected abstract string SerializeRule();
        internal virtual void Attach(CSSStyleSheet? sheet, CSSRule? parent) { ParentStyleSheet = sheet; ParentRule = parent; }
        protected static string Block(string header, string declarations) => header + " {" + (declarations.Length == 0 ? " " : " " + declarations + " ") + "}";
    }
    /// <summary>CSSOM § 6.4.1 live CSSRuleList.</summary>
    public sealed class CSSRuleList : IReadOnlyList<CSSRule>
    {
        private readonly List<CSSRule> _rules = new List<CSSRule>();
        public int Length => _rules.Count;
        public int Count => _rules.Count;
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public CSSRule this[int index] => _rules[index];
        public CSSRule? Item(int index) => index >= 0 && index < Length ? _rules[index] : null;
        internal void Add(CSSRule rule) => _rules.Add(rule);
        internal void Insert(int index, CSSRule rule) => _rules.Insert(index, rule);
        internal CSSRule Remove(int index) { var rule = _rules[index]; _rules.RemoveAt(index); return rule; }
        internal void Clear() => _rules.Clear();
        public IEnumerator<CSSRule> GetEnumerator() => _rules.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    /// <summary>CSSOM § 6.4.5 grouping rule, including nested style rules.</summary>
    public abstract class CSSGroupingRule : CSSRule
    {
        public CSSRuleList CssRules { get; } = new CSSRuleList();
        public int InsertRule(string rule, int index = 0)
        {
            if (index < 0 || index > CssRules.Length) throw new Dom.DomException("Rule index is out of range.", "IndexSizeError");
            var parsed = CSSRuleParser.ParseSingle(rule, ParentStyleSheet, this);
            if (parsed == null) throw new Dom.DomException("Invalid CSS rule.", "SyntaxError");
            if (parsed is CSSImportRule or CSSNamespaceRule || (this is CSSKeyframesRule && parsed is not CSSKeyframeRule)) throw new Dom.DomException("Rule is not allowed in this block.", "HierarchyRequestError");
            parsed.Attach(ParentStyleSheet, this);
            CssRules.Insert(index, parsed);
            ParentStyleSheet?.NotifyChanged();
            return index;
        }
        public void DeleteRule(int index)
        {
            if (index < 0 || index >= CssRules.Length) throw new Dom.DomException("Rule index is out of range.", "IndexSizeError");
            CssRules.Remove(index).Attach(null, null);
            ParentStyleSheet?.NotifyChanged();
        }
        internal override void Attach(CSSStyleSheet? sheet, CSSRule? parent)
        { base.Attach(sheet, parent); foreach (var rule in CssRules) rule.Attach(sheet, this); }
        protected string GroupText(string header, string? declarations = null)
        {
            var result = new StringBuilder(header).Append(" {\n");
            if (!string.IsNullOrEmpty(declarations)) result.Append("  ").Append(declarations).Append('\n');
            foreach (var rule in CssRules) result.Append("  ").Append(rule.CssText.Replace("\n", "\n  ", StringComparison.Ordinal)).Append('\n');
            return result.Append('}').ToString();
        }
    }
}
