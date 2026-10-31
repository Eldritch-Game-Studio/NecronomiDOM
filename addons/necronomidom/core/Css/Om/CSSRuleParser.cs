using System;
using System.Collections.Generic;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Om
{
    internal static partial class CSSRuleParser
    {
        internal static CSSRule? ParseSingle(string css, CSSStyleSheet? sheet, CSSRule? parent)
        {
            var raw = CssParser.ParseRule(css, null, "", out var source);
            if (raw != null) return Convert(raw, sheet, parent, null, "", source);
            if (!InStyle(parent)) return null;
            var diagnostics = new List<WebDiagnostic>();
            var contents = CssParser.ParseBlockContents(css, diagnostics);
            if (diagnostics.Count != 0 || contents.Items.Count == 0) return null;
            foreach (var item in contents.Items) if (item is not RawDeclaration) return null;
            var declarations = new CSSNestedDeclarations();
            declarations.Style.Load(contents);
            declarations.Attach(sheet, parent);
            return declarations;
        }
        internal static CSSRule? Convert(RawRule raw, CSSStyleSheet? sheet, CSSRule? parent, IList<WebDiagnostic>? diagnostics, string file, string? source = null)
        {
            var nested = InStyle(parent);
            CSSRule? result;
            if (raw is RawQualifiedRule qualified)
            {
                if (parent is CSSKeyframesRule)
                {
                    var keys = CSSKeyframesRule.ParseKeys(qualified.Prelude);
                    if (keys == null) return null;
                    var frame = new CSSKeyframeRule(keys);
                    var declarations = new List<object>();
                    foreach (var item in CssParser.ParseBlockContents(qualified.Block.Value, diagnostics, file).Items) if (item is RawDeclaration declaration && !declaration.Important) declarations.Add(declaration);
                    frame.Style.Load(new CssBlockContents(declarations), diagnostics, file);
                    result = frame;
                }
                else
                {
                    var selectors = SelectorParser.ParseSelectorList(qualified.Prelude, new SelectorParseOptions(Relative: nested, Namespaces: sheet?.Namespaces) { SourceText = source });
                    if (selectors == null) return null;
                    if (nested) selectors = SelectorParser.AbsolutizeNestedSelectors(selectors);
                    var style = new CSSStyleRule(selectors);
                    PopulateStyle(style, qualified.Block, sheet, diagnostics, file, source);
                    result = style;
                }
            }
            else if (raw is RawAtRule at)
            {
                if (parent is CSSKeyframesRule) return null;
                result = AtRule(at, sheet, parent, nested, diagnostics, file, source);
            }
            else return null;
            result?.Attach(sheet, parent);
            return result;
        }
        internal static bool InStyle(CSSRule? parent)
        { while (parent != null) { if (parent is CSSStyleRule) return true; parent = parent.ParentRule; } return false; }
        private static void PopulateStyle(CSSStyleRule rule, SimpleBlock block, CSSStyleSheet? sheet, IList<WebDiagnostic>? diagnostics, string file, string? source)
        {
            rule.Attach(sheet, null);
            var pending = new List<object>();
            var nestedSeen = false;
            foreach (var item in CssParser.ParseBlockContents(block.Value, diagnostics, file).Items)
            {
                if (item is RawDeclaration declaration) { pending.Add(declaration); continue; }
                if (pending.Count > 0) { FlushDeclarations(rule, pending, nestedSeen, diagnostics, file); pending.Clear(); }
                if (item is RawRule raw)
                {
                    var child = Convert(raw, sheet, rule, diagnostics, file, source);
                    if (child != null) { rule.CssRules.Add(child); nestedSeen = true; }
                    else Warn(diagnostics, file, raw, "Invalid nested rule.");
                }
            }
            FlushDeclarations(rule, pending, nestedSeen, diagnostics, file);
        }
        private static void FlushDeclarations(CSSStyleRule rule, IReadOnlyList<object> declarations, bool nested, IList<WebDiagnostic>? diagnostics, string file)
        {
            if (declarations.Count == 0) return;
            if (!nested) rule.Style.Load(new CssBlockContents(declarations), diagnostics, file);
            else
            {
                var child = new CSSNestedDeclarations();
                child.Style.Load(new CssBlockContents(declarations), diagnostics, file);
                if (child.Style.Length > 0) { child.Attach(rule.ParentStyleSheet, rule); rule.CssRules.Add(child); }
            }
        }
        private static void PopulateGroup(CSSGroupingRule rule, SimpleBlock block, CSSStyleSheet? sheet, CSSRule? parent, IList<WebDiagnostic>? diagnostics, string file, string? source)
        {
            rule.Attach(sheet, parent);
            if (InStyle(rule))
            {
                var pending = new List<object>();
                foreach (var item in CssParser.ParseBlockContents(block.Value, diagnostics, file).Items)
                {
                    if (item is RawDeclaration declaration) { pending.Add(declaration); continue; }
                    AddNestedDeclarations(rule, pending, diagnostics, file);
                    pending.Clear();
                    if (item is RawRule raw) { var child = Convert(raw, sheet, rule, diagnostics, file, source); if (child != null) rule.CssRules.Add(child); else Warn(diagnostics, file, raw, "Invalid nested rule."); }
                }
                AddNestedDeclarations(rule, pending, diagnostics, file);
            }
            else foreach (var raw in CssParser.ParseRuleList(block.Value))
            {
                var child = Convert(raw, sheet, rule, diagnostics, file, source);
                if (child is CSSImportRule or CSSNamespaceRule) child = null;
                if (child != null) rule.CssRules.Add(child);
                else Warn(diagnostics, file, raw, "Invalid grouped rule.");
            }
        }
        private static void AddNestedDeclarations(CSSGroupingRule parent, IReadOnlyList<object> items, IList<WebDiagnostic>? diagnostics, string file)
        {
            if (items.Count == 0) return;
            var declarations = new CSSNestedDeclarations();
            declarations.Style.Load(new CssBlockContents(items), diagnostics, file);
            if (declarations.Style.Length > 0) { declarations.Attach(parent.ParentStyleSheet, parent); parent.CssRules.Add(declarations); }
        }
        internal static void Warn(IList<WebDiagnostic>? diagnostics, string file, RawRule raw, string message)
        {
            var value = raw.Prelude.Count > 0 ? raw.Prelude[0] : (raw as RawAtRule)?.Block;
            diagnostics?.Add(new WebDiagnostic(WebDiagnosticSeverity.Warning, file, value?.Line ?? 1, value?.Column ?? 1, "css-invalid-rule", message));
        }
    }
}
