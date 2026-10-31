using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values
{
    internal static class CssDescriptorConstraints
    {
        internal static bool Check(string rule, string descriptor, CssValue value)
        {
            if (rule == "counter-style" && descriptor == "additive-symbols")
            {
                double? previous = double.PositiveInfinity;
                foreach (CssValue pair in CommaItems(value))
                {
                    if (pair is not CssListValue { Separator: CssListSeparator.Space } list || list.Items.Count != 2) return false;
                    double? weight = Numeric(list.Items[0], true);
                    if (weight != null) weight = System.Math.Max(0, weight.Value);
                    if (weight != null && previous != null && weight >= previous) return false;
                    if (weight != null) previous = weight;
                }
            }
            if (rule == "counter-style" && descriptor == "range" && value is not CssKeywordValue)
                foreach (CssValue pair in CommaItems(value))
                {
                    if (pair is not CssListValue { Separator: CssListSeparator.Space } list || list.Items.Count != 2) return false;
                    double? lower = list.Items[0] is CssKeywordValue ? double.NegativeInfinity : Numeric(list.Items[0], true);
                    double? upper = list.Items[1] is CssKeywordValue ? double.PositiveInfinity : Numeric(list.Items[1], true);
                    if (lower != null && upper != null && lower > upper) return false;
                }
            if (rule == "font-face" && descriptor is "font-weight" or "font-width" or "font-style" && value is CssListValue { Separator: CssListSeparator.Space } range)
            {
                double? previous = null;
                foreach (CssValue item in range.Items)
                {
                    double? current = Numeric(item);
                    if (current != null && item is CssMathValue)
                        current = descriptor switch
                        {
                            "font-weight" => System.Math.Clamp(current.Value, 1, 1000),
                            "font-width" => System.Math.Max(0, current.Value),
                            _ => System.Math.Clamp(current.Value, -90, 90),
                        };
                    if (current != null && previous > current) return false;
                    if (current != null) previous = current;
                }
            }
            return true;
        }
        private static double? Numeric(CssValue value, bool integer = false)
        {
            double? number = value switch
            {
                CssNumberValue n => n.Value,
                CssPercentageValue p => p.Value,
                CssDimensionValue d => CssNumericRange.Canonical(d.Value, d.Unit),
                CssMathValue math when math.TryEvaluate(CssMathEvaluationContext.PercentageMagnitudes, out double result)
                    && double.IsFinite(result) => result,
                _ => null,
            };
            return integer && number.HasValue ? System.Math.Floor(number.Value + 0.5) : number;
        }
        private static IReadOnlyList<CssValue> CommaItems(CssValue value) => value is CssListValue { Separator: CssListSeparator.Comma } list ? list.Items : new[] { value };
    }
}
