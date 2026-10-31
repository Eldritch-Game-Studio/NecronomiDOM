using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Css.Values.Registry;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private readonly record struct NumericRange(bool Found, double Min, double Max, bool Integer);
        private static readonly ConcurrentDictionary<(string Property, CssNumericDimension Dimension), NumericRange> Ranges = new();

        private static CssValue Constrain(CssValue value, GrammarNode grammar, string property)
        {
            if (value is CssListValue list)
            {
                CssValue[]? changed = null;
                for (int i = 0; i < list.Items.Count; i++)
                {
                    CssValue item = Constrain(list.Items[i], grammar, property);
                    if (changed == null && !ReferenceEquals(item, list.Items[i]))
                    {
                        changed = new CssValue[list.Items.Count];
                        for (int j = 0; j < i; j++) changed[j] = list.Items[j];
                    }
                    if (changed != null) changed[i] = item;
                }
                return changed == null ? list : new CssListValue(changed, list.Separator);
            }
            double number;
            CssNumericDimension dimension;
            switch (value)
            {
                case CssNumberValue n: number = n.Value; dimension = CssNumericDimension.Number; break;
                case CssPercentageValue p: number = p.Value; dimension = CssNumericDimension.Percentage; break;
                case CssDimensionValue d when CssMathUnits.TryGet(d.Unit, out dimension, out _): number = d.Value; break;
                default: return value;
            }
            NumericRange range = RangeFor(property, grammar, dimension);
            double min = range.Found ? range.Min : double.NegativeInfinity;
            double max = range.Found ? range.Max : double.PositiveInfinity;
            if (property is "opacity" or "fill-opacity" or "stroke-opacity" or "flood-opacity" or "stop-opacity") { min = 0; max = 1; }
            if (property == "font-weight") { min = 1; max = 1000; }
            if (double.IsNaN(number)) number = 0;
            number = System.Math.Clamp(number, min, max);
            if (range.Integer) number = System.Math.Floor(number + 0.5);
            if (value is CssNumberValue oldNumber && (oldNumber.Value != number || range.Integer && oldNumber is not CssIntegerValue))
                return range.Integer ? new CssIntegerValue(number) : new CssNumberValue(number, oldNumber.IsInteger);
            if (value is CssPercentageValue oldPercent && oldPercent.Value != number) return new CssPercentageValue(number);
            if (value is CssDimensionValue oldDimension && oldDimension.Value != number) return new CssDimensionValue(number, oldDimension.Unit);
            return value;
        }

        // Zero-as-length canonicalization and numeric clamping ask the same immutable
        // property grammar question. Neither warm path reparses Webref type definitions.
        private static NumericRange RangeFor(string property, GrammarNode grammar, CssNumericDimension dimension)
        {
            var key = (property, dimension);
            if (Ranges.TryGetValue(key, out NumericRange range)) return range;
            range = FindRange(grammar, dimension, property, 0);
            Ranges.TryAdd(key, range);
            return range;
        }

        private static NumericRange FindRange(GrammarNode node, CssNumericDimension dimension, string property, int depth)
        {
            if (depth > 32) return default;
            switch (node)
            {
                case TypeNode type:
                    if (MatchesNumeric(type.Name, dimension)) return new(true, type.Min, type.Max, type.Name == "integer");
                    if (type.IsProperty && CssPropertyRegistry.GetUnfiltered(type.Name) is PropertyDefinition referenced)
                        return FindRange(referenced.Grammar, dimension, property, depth + 1);
                    if (WebrefData.Current.Types.TryGetValue(type.Name, out List<WebrefSyntax>? definitions))
                    {
                        NumericRange result = default;
                        foreach (WebrefSyntax definition in definitions)
                        {
                            if (definition.For.Count != 0 && !Contains(definition.For, property)) continue;
                            NumericRange child = FindRange(GrammarParser.Parse(definition.Syntax), dimension, property, depth + 1);
                            if (child.Found && type.HasRange) child = child with { Min = System.Math.Max(child.Min, type.Min), Max = System.Math.Min(child.Max, type.Max) };
                            result = Union(result, child);
                        }
                        return result;
                    }
                    break;
                case CombinationNode combination:
                    NumericRange union = default;
                    foreach (GrammarNode child in combination.Items) union = Union(union, FindRange(child, dimension, property, depth + 1));
                    return union;
                case MultiplierNode multiplier: return FindRange(multiplier.Child, dimension, property, depth + 1);
                case RequiredNode required: return FindRange(required.Child, dimension, property, depth + 1);
            }
            return default;
        }

        private static bool MatchesNumeric(string name, CssNumericDimension dimension) => dimension switch
        {
            CssNumericDimension.Number => name is "number" or "integer" or "number-percentage",
            CssNumericDimension.Length => name is "length" or "length-percentage",
            CssNumericDimension.Percentage => name is "percentage" or "length-percentage" or "number-percentage" or "angle-percentage",
            CssNumericDimension.Angle => name is "angle" or "angle-percentage",
            CssNumericDimension.Time => name == "time", CssNumericDimension.Frequency => name == "frequency",
            CssNumericDimension.Resolution => name == "resolution", CssNumericDimension.Flex => name == "flex", _ => false
        };
        private static bool Contains(IReadOnlyList<string> names, string name)
        {
            foreach (string item in names) if (item == name) return true;
            return false;
        }
        private static NumericRange Union(NumericRange a, NumericRange b) => !a.Found ? b : !b.Found ? a
            : new(true, System.Math.Min(a.Min, b.Min), System.Math.Max(a.Max, b.Max), a.Integer && b.Integer);
    }
}
