using System.Runtime.CompilerServices;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static readonly ConditionalWeakTable<CssAbsoluteColorValue, ComputedValueColor> AbsoluteColors = new();
        private static CssMixColorValue NormalizeMix(CssMixColorValue mix, string property, ComputedValueContext context)
        {
            CssColorMixItem[]? changed = null;
            for (int i = 0; i < mix.Items.Count; i++)
            {
                CssColorMixItem item = mix.Items[i];
                CssColorValue color = (CssColorValue)ColorValue(item.Color, property, context);
                CssValue? percentage = item.Percentage == null ? null : Normalize(item.Percentage, property, context);
                if (changed == null && (!ReferenceEquals(color, item.Color) || !ReferenceEquals(percentage, item.Percentage)))
                {
                    changed = new CssColorMixItem[mix.Items.Count];
                    for (int j = 0; j < i; j++) changed[j] = mix.Items[j];
                }
                if (changed != null) changed[i] = new CssColorMixItem(color, percentage);
            }
            return changed == null ? mix : new CssMixColorValue(mix.Space, mix.HueMethod, changed);
        }
        private static CssValue ColorValue(CssColorValue color, string property, ComputedValueContext context)
        {
            if (color is ComputedValueColor) return color;
            if (color is CssLightDarkColorValue choice) return ColorValue(context.DarkColorScheme ? choice.Dark : choice.Light, property, context);
            if (color is CssMixColorValue mix) color = NormalizeMix(mix, property, context);
            if (color is CssAlphaColorValue alpha)
            {
                CssColorValue origin = (CssColorValue)ColorValue(alpha.Origin, property, context);
                if (!ReferenceEquals(origin, alpha.Origin)) color = new CssAlphaColorValue(origin, alpha.Alpha);
            }
            if (color is CssRelativeColorValue relative && relative.Origin != null)
            {
                CssColorValue origin = (CssColorValue)ColorValue(relative.Origin, property, context);
                if (!ReferenceEquals(origin, relative.Origin)) color = new CssRelativeColorValue(relative.Space, origin, relative.Channels);
            }
            // Literal absolute colors have no element/font/scheme context. Keep currentcolor and system callbacks live.
            if (color is CssAbsoluteColorValue absolute && absolute.Keyword != "currentcolor" &&
                (absolute.Keyword == null || !CssColorNames.IsSystem(absolute.Keyword)))
                return AbsoluteColors.GetValue(absolute, static value => new ComputedValueColor(value.Color, value.IsLegacyRgb));
            CssValue? source = property == "color" ? context.InheritedColor : context.CurrentColor;
            if (color is CssAbsoluteColorValue { Keyword: "currentcolor" } && source is ComputedValueColor ready) return ready;
            if (color is CssAbsoluteColorValue { Keyword: "currentcolor" } && source is CssColorValue supplied && !supplied.RequiresCurrentColor)
            {
                CssValue suppliedValue = ColorValue(supplied, property, context);
                return suppliedValue is ComputedValueColor ? suppliedValue : color;
            }
            CssResolvedColor current = new("srgb", 0, 0, 0);
            bool available = source is ComputedValueColor computed && Assign(computed.Color, out current);
            if (color.RequiresCurrentColor && !available && source is CssColorValue sourceColor && !sourceColor.RequiresCurrentColor)
                available = sourceColor.TryResolve(new CssColorContext
                {
                    DarkScheme = context.DarkColorScheme, SystemColor = context.SystemColor,
                    NumericContext = ColorNumericContext(property, context)
                }, out current);
            if (color.RequiresCurrentColor && !available) return color;
            var colorContext = new CssColorContext
            {
                CurrentColor = current, DarkScheme = context.DarkColorScheme, SystemColor = context.SystemColor,
                NumericContext = ColorNumericContext(property, context)
            };
            if (!color.TryResolve(colorContext, out CssResolvedColor resolved)) return color;
            bool legacy = color.IsLegacyRgb;
            return new ComputedValueColor(resolved, legacy);
        }
        private static CssMathEvaluationContext ColorNumericContext(string property, ComputedValueContext context) =>
            new(unit => UnitScale(unit, property, context), devicePixelRatio: context.DevicePixelRatio,
                resolveNumericValue: context.NumericValue);
    }
}
