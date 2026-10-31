using System.Collections.Generic;
using NecronomiDom.Css.Selectors;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Nesting 1 § 4 nesting selector semantics, including implicit descendant nesting and :is() specificity.</summary>
    internal static class NestingExpansion
    {
        internal static SelectorList Expand(SelectorList child, SelectorList parent)
        {
            var result = new List<ComplexSelector>(child.Selectors.Count);
            foreach (var selector in child.Selectors)
            {
                bool explicitNesting = HasNesting(selector);
                var compounds = new List<CompoundSelector>();
                var combinators = new List<Combinator>();
                if (!explicitNesting)
                {
                    compounds.Add(new CompoundSelector(new SimpleSelector[] { new PseudoClassSelector("is", parent) }));
                    combinators.Add(selector.LeadingCombinator ?? Combinator.Descendant);
                }
                foreach (var compound in selector.Compounds)
                {
                    var simples = new List<SimpleSelector>(compound.SimpleSelectors.Count);
                    foreach (var simple in compound.SimpleSelectors) simples.Add(Replace(simple, parent));
                    compounds.Add(new CompoundSelector(simples));
                }
                combinators.AddRange(selector.Combinators);
                result.Add(new ComplexSelector(compounds, combinators));
            }
            return new SelectorList(result);
        }
        private static SimpleSelector Replace(SimpleSelector simple, SelectorList parent)
        {
            if (simple is NestingSelector) return new PseudoClassSelector("is", parent);
            if (simple is PseudoClassSelector pseudo)
            {
                SelectorList? args = pseudo.Arguments == null ? null : ReplaceList(pseudo.Arguments, parent);
                NthExpression? nth = pseudo.Nth?.Of == null ? pseudo.Nth : pseudo.Nth with { Of = ReplaceList(pseudo.Nth.Of, parent) };
                return pseudo with { Arguments = args, Nth = nth };
            }
            if (simple is PseudoElementSelector element && element.Arguments != null)
                return element with { Arguments = ReplaceList(element.Arguments, parent) };
            return simple;
        }
        private static SelectorList ReplaceList(SelectorList list, SelectorList parent)
        {
            var selectors = new List<ComplexSelector>();
            foreach (var selector in list.Selectors)
            {
                var compounds = new List<CompoundSelector>();
                foreach (var compound in selector.Compounds)
                {
                    var simple = new List<SimpleSelector>();
                    foreach (var item in compound.SimpleSelectors) simple.Add(Replace(item, parent));
                    compounds.Add(new CompoundSelector(simple));
                }
                selectors.Add(new ComplexSelector(compounds, selector.Combinators, selector.LeadingCombinator));
            }
            return new SelectorList(selectors);
        }
        private static bool HasNesting(ComplexSelector selector)
        {
            foreach (var compound in selector.Compounds)
                foreach (var simple in compound.SimpleSelectors)
                {
                    if (simple is NestingSelector) return true;
                    if (simple is PseudoClassSelector { Arguments: { } args })
                        foreach (var argument in args.Selectors) if (HasNesting(argument)) return true;
                }
            return false;
        }
    }
}
