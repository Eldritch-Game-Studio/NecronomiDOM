using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Images;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Color 4 § 14 computed color, retaining modern color spaces and legacy RGB serialization.</summary>
    public sealed class ComputedValueColor : CssColorValue
    {
        public ComputedValueColor(CssResolvedColor color, bool legacy = false) { Color = color; Legacy = legacy; }
        public CssResolvedColor Color { get; }
        public bool Legacy { get; }
        public override bool RequiresCurrentColor => false;
        public override bool IsLegacyRgb => Legacy;
        public override bool TryResolve(CssColorContext context, out CssResolvedColor color) { color = Color; return true; }
        public override void AppendTo(StringBuilder builder) => Color.AppendComputed(builder, Legacy);
    }

    public static partial class ComputedValueResolver
    {
        private static CssValue NormalizeImage(CssValue value, string property, ComputedValueContext context)
        {
            if (value is CssLightDarkImageValue choice) return Normalize(choice.Select(context.DarkColorScheme), property, context);
            if (value is CssSolidColorImageValue solid)
            {
                var color = (CssColorValue)ColorValue(solid.Color, property, context);
                return ReferenceEquals(color, solid.Color) ? value : new CssSolidColorImageValue(color);
            }
            if (value is CssCrossFadeValue crossFade)
            {
                CssValue from = Normalize(crossFade.From, property, context);
                CssValue to = Normalize(crossFade.To, property, context);
                CssValue progress = crossFade.ProgressFromMath && crossFade.Progress is CssPercentageValue ? crossFade.Progress : Normalize(crossFade.Progress, "opacity", context);
                if (progress is CssNumberValue number)
                {
                    double clamped = System.Math.Clamp(number.Value, 0, 1);
                    if (crossFade.ProgressFromMath) progress = new CssPercentageValue(clamped * 100);
                    else if (clamped != number.Value) progress = new CssNumberValue(clamped, false);
                }
                else if (progress is CssPercentageValue percentage && crossFade.ProgressFromMath)
                {
                    double clamped = System.Math.Clamp(percentage.Value, 0, 100);
                    if (clamped != percentage.Value) progress = new CssPercentageValue(clamped);
                }
                return ReferenceEquals(from, crossFade.From) && ReferenceEquals(to, crossFade.To) && ReferenceEquals(progress, crossFade.Progress)
                    ? value : new CssCrossFadeValue(from, to, progress, crossFade.ProgressFromMath);
            }
            if (value is CssGradientValue gradient)
            {
                CssValue? prelude = gradient.Prelude == null ? null : Normalize(gradient.Prelude, property, context);
                if (prelude != null) prelude = GradientPrelude(prelude, gradient.Name, context);
                CssGradientStop[]? changed = null;
                for (int i = 0; i < gradient.Stops.Count; i++)
                {
                    CssGradientStop stop = gradient.Stops[i];
                    CssColorValue? color = stop.Color == null ? null : (CssColorValue)ColorValue(stop.Color, property, context);
                    CssValue[]? positions = null;
                    for (int j = 0; j < stop.Positions.Count; j++)
                    {
                        CssValue position = Normalize(stop.Positions[j], property, context);
                        if (positions == null && !ReferenceEquals(position, stop.Positions[j]))
                        {
                            positions = new CssValue[stop.Positions.Count];
                            for (int k = 0; k < j; k++) positions[k] = stop.Positions[k];
                        }
                        if (positions != null) positions[j] = position;
                    }
                    if (changed == null && (!ReferenceEquals(color, stop.Color) || positions != null))
                    {
                        changed = new CssGradientStop[gradient.Stops.Count];
                        for (int j = 0; j < i; j++) changed[j] = gradient.Stops[j];
                    }
                    if (changed != null) changed[i] = new CssGradientStop(color, positions ?? stop.Positions);
                }
                return changed == null && ReferenceEquals(prelude, gradient.Prelude) ? value
                    : new CssGradientValue(gradient.Name, prelude, changed ?? gradient.Stops, gradient.InterpolationSpace, gradient.HueMethod);
            }
            if (value is CssImageSetValue imageSet)
            {
                CssImageSetOption[]? changed = null;
                for (int i = 0; i < imageSet.Options.Count; i++)
                {
                    CssImageSetOption option = imageSet.Options[i];
                    CssValue image = Normalize(option.Image, property, context);
                    CssValue? resolution = option.Resolution == null ? new CssDimensionValue(1, "dppx") : Normalize(option.Resolution, property, context);
                    if (resolution is CssDimensionValue { Value: < 0 } negative) resolution = new CssDimensionValue(0, negative.Unit);
                    if (changed == null && (!ReferenceEquals(image, option.Image) || !ReferenceEquals(resolution, option.Resolution)))
                    {
                        changed = new CssImageSetOption[imageSet.Options.Count];
                        for (int j = 0; j < i; j++) changed[j] = imageSet.Options[j];
                    }
                    if (changed != null) changed[i] = new CssImageSetOption(image, resolution, option.MimeType);
                }
                return changed == null ? value : new CssImageSetValue(changed);
            }
            return value;
        }

        private static CssValue Shadow(CssValue value, string property, ComputedValueContext context)
        {
            if (value is CssKeywordValue { Value: "none" }) return value;
            var items = Items(value);
            CssValue? color = null;
            bool inset = false;
            var lengths = new List<CssValue>(4);
            for (int i = 0; i < items.Count; i++)
            {
                CssValue item = items[i];
                if (item is CssColorValue) color = item;
                else if (item is CssKeywordValue { Value: "inset" }) inset = true;
                else lengths.Add(item);
            }
            if (color == null && context.CurrentColor is CssColorValue current) color = ColorValue(current, property, context);
            if (color == null) color = new CssAbsoluteColorValue(default, "currentcolor");
            if (lengths.Count < 2) return value;
            if (lengths.Count < 3) lengths.Add(Pixels(0));
            if (lengths[2] is CssDimensionValue { Unit: "px", Value: < 0 }) lengths[2] = Pixels(0);
            if (property == "box-shadow" && lengths.Count < 4) lengths.Add(Pixels(0));
            var result = new CssValue[lengths.Count + 1 + (inset ? 1 : 0)];
            result[0] = color;
            for (int i = 0; i < lengths.Count; i++) result[i + 1] = lengths[i];
            if (inset) result[result.Length - 1] = Keyword("inset");
            return new CssListValue(result, CssListSeparator.Space);
        }

        private static CssValue Filter(CssValue value, ComputedValueContext context)
        {
            if (value is CssListValue list)
            {
                CssValue[]? changed = null;
                for (int i = 0; i < list.Items.Count; i++)
                {
                    CssValue item = Filter(list.Items[i], context);
                    if (changed == null && !ReferenceEquals(item, list.Items[i]))
                    {
                        changed = new CssValue[list.Items.Count];
                        for (int j = 0; j < i; j++) changed[j] = list.Items[j];
                    }
                    if (changed != null) changed[i] = item;
                }
                return changed == null ? value : new CssListValue(changed, list.Separator);
            }
            if (value is not CssFunctionValue function) return value;
            if (function.Name == "drop-shadow" && function.Arguments != null) return new CssFunctionValue(function.Name, Shadow(function.Arguments, "text-shadow", context));
            if (function.Name is not ("blur" or "hue-rotate" or "grayscale" or "sepia" or "invert" or "opacity" or "brightness" or "contrast" or "saturate")) return value;
            CssValue? argument = function.Arguments;
            if (argument == null || argument is CssListValue { Items.Count: 0 })
                argument = function.Name is "blur" ? Pixels(0) : function.Name is "hue-rotate" ? new CssDimensionValue(0, "deg") : new CssNumberValue(1, false);
            if (function.Name is "grayscale" or "sepia" or "invert" or "opacity" or "brightness" or "contrast" or "saturate")
            {
                if (argument is Values.Math.CssMathValue math) argument = MathValue(math, "opacity", context);
                double? number = argument is CssPercentageValue percent ? percent.Value / 100 : argument is CssNumberValue numeric ? numeric.Value : null;
                if (number.HasValue)
                {
                    double maximum = function.Name is "brightness" or "contrast" or "saturate" ? double.MaxValue : 1;
                    double clamped = System.Math.Clamp(number.Value, 0, maximum);
                    if (argument is not CssNumberValue existing || existing.Value != clamped) argument = new CssNumberValue(clamped, false);
                }
            }
            if (function.Name == "blur" && (argument is CssDimensionValue { Unit: "px", Value: < 0 } || argument is CssNumberValue { Value: 0 })) argument = Pixels(0);
            if (function.Name == "hue-rotate" && argument is CssNumberValue { Value: 0 }) argument = new CssDimensionValue(0, "deg");
            return ReferenceEquals(argument, function.Arguments) ? value : new CssFunctionValue(function.Name, argument);
        }
    }
}
