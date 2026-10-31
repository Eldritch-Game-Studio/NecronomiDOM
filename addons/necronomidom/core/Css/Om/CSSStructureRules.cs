using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Om
{
    /// <summary>CSS Cascade 5 @layer block; empty name denotes an anonymous layer.</summary>
    public sealed class CSSLayerBlockRule : CSSGroupingRule
    {
        public CSSLayerBlockRule(string name) => Name = name;
        public string Name { get; }
        public override int Type => 0;
        protected override string SerializeRule() => GroupText("@layer" + (Name.Length == 0 ? "" : " " + Name));
    }
    /// <summary>CSS Cascade 5 @layer order statement.</summary>
    public sealed class CSSLayerStatementRule : CSSRule
    {
        public CSSLayerStatementRule(IReadOnlyList<string> names) => NameList = names;
        public IReadOnlyList<string> NameList { get; }
        public override int Type => 0;
        protected override string SerializeRule()
        {
            var result = new StringBuilder("@layer ");
            for (var i = 0; i < NameList.Count; i++) { if (i > 0) result.Append(", "); result.Append(NameList[i]); }
            return result.Append(';').ToString();
        }
    }
    /// <summary>CSS Cascade 6 § 2.5 @scope boundaries and enclosed rules.</summary>
    public sealed class CSSScopeRule : CSSGroupingRule
    {
        public CSSScopeRule(SelectorList? start, SelectorList? end) { Start = start; End = end; }
        public SelectorList? Start { get; }
        public SelectorList? End { get; }
        public override int Type => 0;
        protected override string SerializeRule() => GroupText("@scope" + (Start == null ? "" : " (" + Start.Serialize() + ")") + (End == null ? "" : " to (" + End.Serialize() + ")"));
    }
    /// <summary>CSSOM § 6.4.4 @import URL, optional cascade layer/supports and media.</summary>
    public sealed class CSSImportRule : CSSRule
    {
        public CSSImportRule(string href, string? layerName, QueryCondition? supports, MediaQueryList media)
        { Href = href; LayerName = layerName; Supports = supports; Media = new MediaList(media) { Changed = () => ParentStyleSheet?.NotifyChanged() }; }
        public string Href { get; }
        public string? LayerName { get; }
        public QueryCondition? Supports { get; }
        public string? SupportsText => Supports?.Serialize();
        public MediaList Media { get; }
        private CSSStyleSheet? _styleSheet;
        public CSSStyleSheet? StyleSheet { get => _styleSheet; set { _styleSheet = value; if (value != null) { value.OwnerRule = this; value.ParentStyleSheet = ParentStyleSheet; } ParentStyleSheet?.NotifyChanged(); } }
        public override int Type => 3;
        protected override string SerializeRule() => "@import " + CssSerialization.SerializeUrl(Href) + (LayerName == null ? "" : LayerName.Length == 0 ? " layer" : " layer(" + LayerName + ")") +
            (Supports == null ? "" : " supports(" + (Supports is DeclarationCondition declaration ? declaration.Property + ": " + declaration.Value : Supports.Serialize()) + ")") + (Media.Length == 0 ? "" : " " + Media.MediaText) + ";";
        internal override void Attach(CSSStyleSheet? sheet, CSSRule? parent) { base.Attach(sheet, parent); if (_styleSheet != null) _styleSheet.ParentStyleSheet = sheet; }
    }
    /// <summary>CSS Namespaces 3 @namespace prefix-to-URL binding.</summary>
    public sealed class CSSNamespaceRule : CSSRule
    {
        public CSSNamespaceRule(string prefix, string namespaceUri) { Prefix = prefix; NamespaceURI = namespaceUri; }
        public string Prefix { get; }
        public string NamespaceURI { get; }
        public override int Type => 10;
        protected override string SerializeRule() => "@namespace " + (Prefix.Length == 0 ? "" : CssSerialization.SerializeIdentifier(Prefix) + " ") + CssSerialization.SerializeUrl(NamespaceURI) + ";";
    }
    /// <summary>Media Queries 5 @custom-media definition preserved for stylesheet-level expansion.</summary>
    public sealed class CSSCustomMediaRule : CSSRule
    {
        public CSSCustomMediaRule(string name, MediaQueryList? queries, bool? booleanValue = null) { Name = name; Queries = queries; BooleanValue = booleanValue; }
        public string Name { get; }
        public MediaQueryList? Queries { get; }
        public bool? BooleanValue { get; }
        public override int Type => 0;
        protected override string SerializeRule() => "@custom-media " + CssSerialization.SerializeIdentifier(Name) + " " + (BooleanValue.HasValue ? BooleanValue.Value ? "true" : "false" : Queries!.Serialize()) + ";";
    }
}
