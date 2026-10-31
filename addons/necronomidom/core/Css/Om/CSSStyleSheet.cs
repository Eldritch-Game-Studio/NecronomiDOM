using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Url;

namespace NecronomiDom.Css.Om
{
    /// <summary>CSS Cascade 5 stylesheet origins.</summary>
    public enum CssOrigin { UserAgent, User, Author }
    /// <summary>CSSOM § 6.1/6.2 stylesheet parsing, constructed replacement and ordered rule mutation.</summary>
    public sealed class CSSStyleSheet
    {
        public CSSStyleSheet() { Media = new MediaList { Changed = NotifyChanged }; }
        private bool _disabled;
        private CssOrigin _origin = CssOrigin.Author;
        public CSSRuleList CssRules { get; } = new CSSRuleList();
        public bool IsConstructed { get; private set; } = true;
        public bool Disabled { get => _disabled; set { if (_disabled != value) { _disabled = value; NotifyChanged(); } } }
        public CssOrigin Origin { get => _origin; set { if (_origin != value) { _origin = value; NotifyChanged(); } } }
        public string? Href { get; private set; }
        public Dom.Node? OwnerNode { get; set; }
        internal void SetInlineOwner(Dom.Node owner) { OwnerNode = owner; Href = null; }
        public MediaList Media { get; }
        public string Title { get; set; } = "";
        public string Type => "text/css";
        public CSSImportRule? OwnerRule { get; internal set; }
        public CSSStyleSheet? ParentStyleSheet { get; internal set; }
        public WebUrl? BaseUrl { get; private set; }
        public string SourcePath { get; private set; } = "";
        public long Revision { get; private set; }
        private static long _mutationEpoch;
        internal static long MutationEpoch => System.Threading.Interlocked.Read(ref _mutationEpoch);
        internal Dictionary<string, string> Namespaces { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        internal void NotifyChanged()
        {
            Revision++;
            System.Threading.Interlocked.Increment(ref _mutationEpoch);
            ParentStyleSheet?.NotifyChanged();
        }
        public static CSSStyleSheet Parse(string css, string sourcePath = "", WebUrl? baseUrl = null, IList<WebDiagnostic>? diagnostics = null)
        {
            var sheet = new CSSStyleSheet { IsConstructed = false, Href = baseUrl?.Href, BaseUrl = baseUrl, SourcePath = sourcePath };
            sheet.Load(css, diagnostics, false);
            return sheet;
        }
        public int InsertRule(string rule, int index = 0)
        {
            if (index < 0 || index > CssRules.Length) throw new Dom.DomException("Rule index is out of range.", "IndexSizeError");
            var parsed = CSSRuleParser.ParseSingle(rule, this, null);
            if (parsed == null) throw new Dom.DomException("Invalid CSS rule.", "SyntaxError");
            if (IsConstructed && parsed is CSSImportRule) throw new Dom.DomException("Constructed stylesheets cannot import.", "SyntaxError");
            CheckInsertion(parsed, index);
            parsed.Attach(this, null);
            CssRules.Insert(index, parsed);
            RebuildNamespaces();
            NotifyChanged();
            return index;
        }
        public void DeleteRule(int index)
        {
            if (index < 0 || index >= CssRules.Length) throw new Dom.DomException("Rule index is out of range.", "IndexSizeError");
            var rule = CssRules[index];
            if (rule is CSSNamespaceRule)
                foreach (var existing in CssRules) if (existing is not (CSSImportRule or CSSNamespaceRule or CSSLayerStatementRule)) throw new Dom.DomException("Namespace bindings are in use.", "InvalidStateError");
            CssRules.Remove(index).Attach(null, null);
            RebuildNamespaces();
            NotifyChanged();
        }
        public void ReplaceSync(string text)
        {
            if (!IsConstructed) throw new Dom.DomException("Only constructed stylesheets can be replaced.", "NotAllowedError");
            Load(text, null, true);
            NotifyChanged();
        }
        private void Load(string css, IList<WebDiagnostic>? diagnostics, bool removeImports)
        {
            foreach (var rule in CssRules) rule.Attach(null, null);
            CssRules.Clear();
            Namespaces.Clear();
            var rules = CssParser.ParseStylesheet(css, diagnostics, SourcePath, out var source);
            foreach (var raw in rules)
            {
                var rule = CSSRuleParser.Convert(raw, this, null, diagnostics, SourcePath, source);
                if (rule == null) { CSSRuleParser.Warn(diagnostics, SourcePath, raw, "Invalid or unsupported CSS rule."); continue; }
                if (removeImports && rule is CSSImportRule) continue;
                if (!AllowedAtEnd(rule)) { CSSRuleParser.Warn(diagnostics, SourcePath, raw, "At-rule is not allowed after these rules."); continue; }
                rule.Attach(this, null);
                CssRules.Add(rule);
                if (rule is CSSNamespaceRule ns) Namespaces[ns.Prefix] = ns.NamespaceURI;
            }
        }
        private bool AllowedAtEnd(CSSRule rule)
        {
            if (rule is CSSImportRule)
            { foreach (var existing in CssRules) if (existing is not (CSSImportRule or CSSLayerStatementRule)) return false; }
            if (rule is CSSNamespaceRule)
            { foreach (var existing in CssRules) if (existing is not (CSSImportRule or CSSNamespaceRule or CSSLayerStatementRule)) return false; }
            return true;
        }
        private void CheckInsertion(CSSRule rule, int index)
        {
            if (rule is CSSNamespaceRule)
                foreach (var existing in CssRules) if (existing is not (CSSImportRule or CSSNamespaceRule or CSSLayerStatementRule)) throw new Dom.DomException("Namespace bindings are in use.", "InvalidStateError");
            for (var i = 0; i < index; i++)
            {
                if (rule is CSSImportRule && CssRules[i] is not (CSSImportRule or CSSLayerStatementRule)) throw new Dom.DomException("Imports must precede other rules.", "HierarchyRequestError");
                if (rule is CSSNamespaceRule && CssRules[i] is not (CSSImportRule or CSSNamespaceRule or CSSLayerStatementRule)) throw new Dom.DomException("Namespaces must precede style rules.", "HierarchyRequestError");
            }
            for (var i = index; i < CssRules.Length; i++)
            {
                if (CssRules[i] is CSSImportRule && rule is not (CSSImportRule or CSSLayerStatementRule)) throw new Dom.DomException("Cannot precede an import.", "HierarchyRequestError");
                if (CssRules[i] is CSSNamespaceRule && rule is not (CSSImportRule or CSSNamespaceRule or CSSLayerStatementRule)) throw new Dom.DomException("Cannot precede a namespace.", "HierarchyRequestError");
            }
        }
        private void RebuildNamespaces()
        { Namespaces.Clear(); foreach (var rule in CssRules) if (rule is CSSNamespaceRule ns) Namespaces[ns.Prefix] = ns.NamespaceURI; }
    }
}
