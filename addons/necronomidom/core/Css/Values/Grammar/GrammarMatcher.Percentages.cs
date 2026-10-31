using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Grammar
{
    internal sealed partial class GrammarMatcher
    {
        private static readonly ConcurrentDictionary<string, CssNumericDimension?> PercentageContexts = new(System.StringComparer.Ordinal);

        private CssNumericDimension? PercentageContext()
        {
            if (PercentageContexts.TryGetValue(_scope, out CssNumericDimension? cached)) return cached;
            PropertyDefinition? property = CssPropertyRegistry.GetUnfiltered(_scope);
            CssNumericDimension? context = property == null ? null : FindPercentageContext(property.Grammar, new HashSet<GrammarNode>());
            PercentageContexts.TryAdd(_scope, context);
            return context;
        }

        // A function's argument types do not grant percentage support to the enclosing property's numeric branches.
        private CssNumericDimension? FindPercentageContext(GrammarNode node, HashSet<GrammarNode> visited)
        {
            if (!visited.Add(node)) return null;
            if (node is TypeNode type)
            {
                if (!type.IsProperty)
                {
                    if (type.Name == "length-percentage") return CssNumericDimension.Length;
                    if (type.Name == "angle-percentage") return CssNumericDimension.Angle;
                    if (type.Name is "number-percentage" or "opacity-value") return CssNumericDimension.Number;
                    if (type.Name == "percentage") return CssNumericDimension.Percentage;
                    if (IsPrimitive(type.Name) || type.Name.EndsWith("()", System.StringComparison.Ordinal)) return null;
                }
                CssNumericDimension? found = null;
                foreach (GrammarNode reference in Resolve(type)) found = Merge(found, FindPercentageContext(reference, visited));
                return found;
            }
            if (node is MultiplierNode multiplier) return FindPercentageContext(multiplier.Child, visited);
            if (node is RequiredNode required) return FindPercentageContext(required.Child, visited);
            if (node is CombinationNode combination)
            {
                CssNumericDimension? found = null;
                foreach (GrammarNode child in combination.Items) found = Merge(found, FindPercentageContext(child, visited));
                return found;
            }
            return null;
        }

        private static CssNumericDimension? Merge(CssNumericDimension? current, CssNumericDimension? next)
            => next == null ? current : current == null || current == CssNumericDimension.Percentage ? next : current;
    }
}
