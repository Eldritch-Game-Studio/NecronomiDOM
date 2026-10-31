using System;
using System.Runtime.CompilerServices;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

/// <summary>The existing bounded native stylesheet/selector profile. CSSOM revisions invalidate validation;
/// unchanged sheets are not walked or re-serialized on every layout/visibility update.</summary>
public static class RuntimeStyleProfile
{
    private sealed class Validation { internal long Revision = -1; }
    private static readonly ConditionalWeakTable<CSSStyleSheet, Validation> Validated = new();

    public static void Validate(Document document)
    {
        var sheets = ((DocumentStyleSheetList)document.StyleSheets).Snapshot();
        for (int i = 0; i < sheets.Count; i++) Validate(sheets[i]);
        var adopted = document.AdoptedStyleSheets;
        for (int i = 0; i < adopted.Count; i++) Validate(adopted[i]);
    }
    private static void Validate(CSSStyleSheet sheet)
    {
        var state = Validated.GetValue(sheet, static _ => new Validation());
        if (state.Revision == sheet.Revision) return;
        ValidateRules(sheet.CssRules);
        state.Revision = sheet.Revision;
    }
    private static void ValidateRules(CSSRuleList rules)
    {
        for (int i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            if (rule is not CSSStyleRule && rule is not CSSMediaRule && rule is not CSSNestedDeclarations && rule is not CSSFontFaceRule)
                throw new NotSupportedException("Runtime stylesheet rule: " + rule.GetType().Name);
            if (rule is CSSStyleRule style) ValidateSelectors(style.Selectors);
            if (rule is CSSGroupingRule group) ValidateRules(group.CssRules);
        }
    }
    private static void ValidateSelectors(SelectorList list)
    {
        for (int i = 0; i < list.Selectors.Count; i++)
        {
            var selector = list.Selectors[i];
            for (int j = 0; j < selector.Compounds.Count; j++)
            {
                var compound = selector.Compounds[j];
                for (int k = 0; k < compound.SimpleSelectors.Count; k++)
                {
                    var simple = compound.SimpleSelectors[k];
                    if (simple is PseudoElementSelector) throw new NotSupportedException("Runtime pseudo-element: " + simple.Serialize());
                    if (simple is not PseudoClassSelector pseudo) continue;
                    if (pseudo.Name is not ("root" or "empty" or "first-child" or "last-child" or "only-child" or "nth-child" or "nth-last-child"
                        or "first-of-type" or "last-of-type" or "only-of-type" or "nth-of-type" or "nth-last-of-type"
                        or "is" or "not" or "where" or "has" or "scope" or "enabled" or "disabled"
                        or "focus" or "focus-visible" or "focus-within" or "hover" or "active"))
                        throw new NotSupportedException("Runtime state selector: " + pseudo.Serialize());
                    if (pseudo.Arguments != null) ValidateSelectors(pseudo.Arguments);
                    if (pseudo.Nth?.Of != null) ValidateSelectors(pseudo.Nth.Of);
                }
            }
        }
    }
}
