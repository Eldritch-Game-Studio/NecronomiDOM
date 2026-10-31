using System;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue FontValue(string property, CssValue value, ComputedValueContext context)
        {
            if (property == "font-size")
            {
                double? parent = context.IsRoot ? context.InitialFont.Size : context.ParentFont.Size;
                if (value is CssPercentageValue percent && parent.HasValue) return Pixels(System.Math.Max(0, parent.Value * percent.Value / 100));
                if (value is CssKeywordValue size)
                {
                    double? medium = context.MediumFontSize ?? context.InitialFont.Size;
                    double ratio = size.Value switch
                    {
                        "xx-small" => 9.0 / 16, "x-small" => 10.0 / 16, "small" => 13.0 / 16,
                        "medium" => 1, "large" => 18.0 / 16, "x-large" => 1.5, "xx-large" => 2,
                        "xxx-large" => 3, _ => double.NaN
                    };
                    if (medium.HasValue && !double.IsNaN(ratio)) return Pixels(medium.Value * ratio);
                    if (parent.HasValue && size.Value is "larger" or "smaller")
                        return Pixels(parent.Value * (size.Is("larger") ? 1.2 : 1 / 1.2));
                    if (parent.HasValue && size.Is("math") && context.MathFontSizeScale.HasValue)
                        return Pixels(parent.Value * context.MathFontSizeScale.Value);
                }
            }
            if (property == "line-height" && value is CssPercentageValue height && context.Font.Size.HasValue)
                return Pixels(System.Math.Max(0, context.Font.Size.Value * height.Value / 100));
            if (property == "font-weight" && value is CssKeywordValue weight)
            {
                if (weight.Is("normal")) return new CssNumberValue(400, false);
                if (weight.Is("bold")) return new CssNumberValue(700, false);
                if (context.ParentFontWeight is double inherited)
                {
                    if (weight.Is("bolder")) return new CssNumberValue(inherited < 350 ? 400 : inherited < 550 ? 700 : System.Math.Max(900, inherited), false);
                    if (weight.Is("lighter")) return new CssNumberValue(inherited < 550 ? System.Math.Min(100, inherited) : inherited < 750 ? 400 : 700, false);
                }
            }
            if (property is "font-width" or "font-stretch" && value is CssKeywordValue width)
            {
                double percent = CssFontWidthValues.Percentage(width.Value);
                if (!double.IsNaN(percent)) return new CssPercentageValue(percent);
            }
            return value;
        }

    }
}
