using System;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static double? UnitScale(string unit, string property, ComputedValueContext context)
        {
            if (CssMathUnits.TryGet(unit, out _, out double? absolute) && absolute.HasValue) return absolute;
            unit = unit.ToLowerInvariant();
            bool fontAffecting = IsFontAffecting(property);
            ComputedFontMetrics font = fontAffecting ? (context.IsRoot ? context.InitialFont : context.ParentFont) : context.Font;
            ComputedFontMetrics root = fontAffecting && context.IsRoot ? context.InitialFont : context.RootFont;
            switch (unit)
            {
                case "em": return font.Size;
                case "ex": return font.Ex;
                case "ch": return font.Ch;
                case "cap": return font.Cap;
                case "ic": return font.Ic;
                case "lh": return fontAffecting || property == "line-height" ? (context.IsRoot ? context.InitialFont.LineHeight : context.ParentFont.LineHeight) : font.LineHeight;
                case "rem": return root.Size;
                case "rex": return root.Ex;
                case "rch": return root.Ch;
                case "rcap": return root.Cap;
                case "ric": return root.Ic;
                case "rlh": return (fontAffecting || property == "line-height") && context.IsRoot ? context.InitialFont.LineHeight : root.LineHeight;
            }
            context.ViewportUnitRead?.Invoke();
            if (unit.StartsWith("cq", StringComparison.Ordinal))
            {
                if (unit is "cqmin" or "cqmax")
                {
                    double? inline = ContainerBasis("cqi", context), block = ContainerBasis("cqb", context);
                    return inline.HasValue && block.HasValue ? (unit == "cqmin" ? System.Math.Min(inline.Value, block.Value) : System.Math.Max(inline.Value, block.Value)) / 100 : null;
                }
                return ContainerBasis(unit, context) / 100;
            }
            ComputedViewportMetrics viewport = context.LargeViewport;
            if (!viewport.Width.HasValue && !viewport.Height.HasValue) viewport = context.Viewport;
            if (unit.StartsWith('s')) { viewport = context.SmallViewport; unit = unit.Substring(1); }
            else if (unit.StartsWith('l')) { viewport = context.LargeViewport; unit = unit.Substring(1); }
            else if (unit.StartsWith('d')) { viewport = context.DynamicViewport; unit = unit.Substring(1); }
            return ViewportBasis(unit, viewport) / 100;
        }

        // Font-relative units in font-affecting properties use the parent's metrics to avoid cycles.
        private static bool IsFontAffecting(string property) =>
            property.StartsWith("font-", StringComparison.Ordinal) || property is "font" or "math-depth" or "math-style";

        // Values 4 § 5: out-of-range angles clamp to a supported whole number of turns.
        // This exact multiple also leaves ample headroom for downstream radians conversion.
        private const double MaximumAngle = 360.0 * 4503599627370496.0;

        private static double DimensionNumber(double number, CssNumericDimension dimension)
        {
            if (double.IsNaN(number)) return 0;
            if (dimension == CssNumericDimension.Angle)
                return System.Math.Clamp(number, -MaximumAngle, MaximumAngle);
            return double.IsInfinity(number) ? System.Math.CopySign(double.MaxValue, number) : number;
        }

        private static double? ContainerBasis(string unit, ComputedValueContext context)
        {
            double? basis = context.ContainerUnitBasis?.Invoke(unit);
            if (basis.HasValue || !context.NoEligibleContainer) return basis;
            return ViewportBasis(unit switch { "cqw" => "vw", "cqh" => "vh", "cqi" => "vi", "cqb" => "vb", _ => unit }, context.SmallViewport);
        }

        private static double? ViewportBasis(string unit, ComputedViewportMetrics viewport) => unit switch
        {
            "vw" or "vi" => viewport.Width,
            "vh" or "vb" => viewport.Height,
            "vmin" when viewport.Width.HasValue && viewport.Height.HasValue => System.Math.Min(viewport.Width.Value, viewport.Height.Value),
            "vmax" when viewport.Width.HasValue && viewport.Height.HasValue => System.Math.Max(viewport.Width.Value, viewport.Height.Value),
            _ => null
        };

        private static CssValue Dimension(CssDimensionValue value, string property, ComputedValueContext context)
        {
            if (!CssMathUnits.TryGet(value.Unit, out CssNumericDimension dimension, out _)) return value;
            if (value.Value == 0 || double.IsNaN(value.Value)) return value.Value == 0 && value.Unit == CssMathUnits.Canonical(dimension) ? value : new CssDimensionValue(0, CssMathUnits.Canonical(dimension));
            double? scale = UnitScale(value.Unit, property, context);
            if (scale == 1 && value.Unit == CssMathUnits.Canonical(dimension) && DimensionNumber(value.Value, dimension) == value.Value) return value;
            return scale.HasValue ? new CssDimensionValue(DimensionNumber(value.Value * scale.Value, dimension), CssMathUnits.Canonical(dimension)) : value;
        }

        private static CssValue MathValue(CssMathValue math, string property, ComputedValueContext context)
        {
            double? percentage = property == "font-size" ? (context.IsRoot ? context.InitialFont.Size : context.ParentFont.Size)
                : property == "line-height" ? context.Font.Size : IsNumberPercentage(property) ? 1 : null;
            var evaluation = new CssMathEvaluationContext(unit => UnitScale(unit, property, context), percentage ?? (math.Type.IsPercentage ? 100 : null),
                percentageHint: IsNumberPercentage(property) ? CssNumericDimension.Number : percentage.HasValue ? CssNumericDimension.Length : null,
                devicePixelRatio: context.DevicePixelRatio, resolveNumericValue: context.NumericValue);
            if (!math.TryEvaluate(evaluation, out double number))
            {
                CssMathValue simplified = math.Simplify(evaluation);
                if (simplified.Root is CssMathLiteral { IsUnbounded: false } literal && double.IsFinite(literal.Value))
                {
                    if (literal.Unit == "%") return new CssPercentageValue(MathGrammarNumber(math, literal.Value));
                    if (literal.Unit.Length == 0) return MathScalar(math, literal.Value);
                    if (CssMathUnits.TryGet(literal.Unit, out _, out double? scale) && scale.HasValue)
                    {
                        var converted = (CssDimensionValue)Dimension(new CssDimensionValue(literal.Value, literal.Unit), property, context);
                        double bounded = MathGrammarNumber(math, converted.Value);
                        return bounded == converted.Value ? converted : new CssDimensionValue(bounded, converted.Unit);
                    }
                }
                return simplified;
            }
            number = MathGrammarNumber(math, number);
            string unit = math.Type.CanonicalUnit;
            if (unit == "%" && !percentage.HasValue) return new CssPercentageValue(number);
            if (unit == "%" && percentage.HasValue) unit = IsNumberPercentage(property) ? "" : "px";
            if (unit.Length == 0) return math.IsIntegerTarget ? new CssIntegerValue(number) : new CssNumberValue(number, false);
            return new CssDimensionValue(number, unit);
        }

        private static CssValue MathScalar(CssMathValue math, double number)
        {
            number = MathGrammarNumber(math, number);
            return math.IsIntegerTarget ? new CssIntegerValue(number) : new CssNumberValue(number, false);
        }

        private static double MathGrammarNumber(CssMathValue math, double number)
        {
            if (double.IsNaN(number)) number = 0;
            if (math.Type.SamePowers(CssNumericType.For(CssNumericDimension.Angle)))
                number = DimensionNumber(number, CssNumericDimension.Angle);
            else if (double.IsInfinity(number)) number = double.IsPositiveInfinity(number) ? double.MaxValue : -double.MaxValue;
            number = System.Math.Clamp(number, math.GrammarMinimum, math.GrammarMaximum);
            if (!math.IsIntegerTarget) return number;
            double lower = System.Math.Floor(number);
            return number - lower < 0.5 ? lower : lower + 1;
        }

        private static bool IsNumberPercentage(string property) => property is "opacity" or "fill-opacity" or "stroke-opacity" or "flood-opacity" or "stop-opacity" or "scale";
    }
}
