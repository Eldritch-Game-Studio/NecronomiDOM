using System.Collections.Generic;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorParser
    {
        /// <summary>CSS Nesting 1 § 3 and § 6.1 absolutize relative selectors for CSSStyleRule serialization.</summary>
        internal static SelectorList AbsolutizeNestedSelectors(SelectorList source)
        {
            var selectors = new List<ComplexSelector>(source.Selectors.Count);
            foreach (var selector in source.Selectors)
            {
                if ((selector.LeadingCombinator == null || selector.LeadingCombinator == Combinator.Descendant) && HasNesting(selector))
                {
                    selectors.Add(new ComplexSelector(selector.Compounds, selector.Combinators));
                    continue;
                }
                var compounds = new List<CompoundSelector>(selector.Compounds.Count + 1)
                { new CompoundSelector(new SimpleSelector[] { new NestingSelector() }) };
                compounds.AddRange(selector.Compounds);
                var combinators = new List<Combinator>(selector.Combinators.Count + 1)
                { selector.LeadingCombinator ?? Combinator.Descendant };
                combinators.AddRange(selector.Combinators);
                selectors.Add(new ComplexSelector(compounds, combinators));
            }
            return new SelectorList(selectors);
        }
        private static bool HasNesting(ComplexSelector selector)
        {
            foreach (var compound in selector.Compounds)
                foreach (var simple in compound.SimpleSelectors)
                {
                    if (simple is NestingSelector || simple is InvalidSelector { ContainsNesting: true }) return true;
                    if (simple is PseudoClassSelector pseudo && (HasNesting(pseudo.Arguments) || HasNesting(pseudo.Nth?.Of))) return true;
                    if (simple is PseudoElementSelector element && HasNesting(element.Arguments)) return true;
                }
            return false;
        }
        private static bool HasNesting(SelectorList? list)
        { if (list == null) return false; foreach (var selector in list.Selectors) if (HasNesting(selector)) return true; return false; }
    }
}
