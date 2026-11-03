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
    /// <summary>Validate an unattached candidate sheet before publishing it to a live document.</summary>
    public static void Validate(CSSStyleSheet sheet)
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
            if (!RuntimeCapabilities.AdmitsStylesheetRule(rule.GetType().Name))
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
                    if (!RuntimeCapabilities.AdmitsPseudoClass(pseudo.Name))
                        throw new NotSupportedException("Runtime state selector: " + pseudo.Serialize());
                    if (pseudo.Arguments != null) ValidateSelectors(pseudo.Arguments);
                    if (pseudo.Nth?.Of != null) ValidateSelectors(pseudo.Nth.Of);
                }
            }
        }
    }
}
