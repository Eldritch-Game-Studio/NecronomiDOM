using System;
using System.Collections.Concurrent;

using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        // Registry-canonical longhand names are finite; avoid allocating their companion style name per element.
        private static readonly ConcurrentDictionary<string, string> WidthStyleNames = new(StringComparer.Ordinal);
        private static CssValue PropertyValue(string property, CssValue value, PropertyDefinition definition, in ComputedValueContext context)
        {
            if (property == "transform") return Transform(value);
            if (property is "transform-origin" or "perspective-origin" or "object-position" or "background-position" or "mask-position")
                return Position(value, property, context);
            if (property == "rotate") return CssTransformSpecifiedValues.Rotate(value);
            if (property is "border-spacing" && Items(value).Count == 1) return Space(value, value);
            if (property.EndsWith("-size", StringComparison.Ordinal) && property is "background-size" or "mask-size")
                return Layers(value, FillSize);
            if (property is "background-repeat" or "mask-repeat") return Layers(value, RepeatPair);
            if (property is "border-image-outset" or "border-image-width" or "border-image-slice") return FourSides(value);
            if (property is "border-image-repeat" or "mask-border-repeat" && Items(value).Count == 2)
            {
                var repeated = Items(value);
                if (EqualValue(repeated[0], repeated[1])) return repeated[0];
            }
            if (property is "box-shadow" or "text-shadow")
                return value is CssListValue { Separator: CssListSeparator.Comma }
                    ? ShadowLayers(value, property, context) : Shadow(value, property, context);
            if (property is "filter" or "backdrop-filter") return Filter(value, context);
            if (property == "display") return Display(value, context);
            if (property is "overflow-x" or "overflow-y" or "overflow-inline" or "overflow-block" && value is CssKeywordValue overflow)
            {
                string counterpart = property switch { "overflow-x" => "overflow-y", "overflow-y" => "overflow-x", "overflow-inline" => "overflow-block", _ => "overflow-inline" };
                CssValue? other = context.PropertyValue?.Invoke(counterpart);
                if (other is CssKeywordValue otherOverflow && !otherOverflow.Is("visible") && !otherOverflow.Is("clip"))
                {
                    if (overflow.Is("visible")) return Keyword("auto");
                }
            }
            if (property == "text-align" && value is CssKeywordValue align && align.Is("match-parent"))
            {
                CssValue? inherited = context.ParentPropertyValue?.Invoke(property);
                if (inherited is CssKeywordValue parentAlign)
                {
                    bool rtl = context.ParentPropertyValue?.Invoke("direction") is CssKeywordValue dir && dir.Is("rtl");
                    return parentAlign.Value switch { "start" => Keyword(rtl ? "right" : "left"), "end" => Keyword(rtl ? "left" : "right"), _ => inherited };
                }
            }
            if (property.EndsWith("-width", StringComparison.Ordinal) && definition.ComputedValue.Contains("absolute length", StringComparison.OrdinalIgnoreCase))
            {
                if (value is CssKeywordValue width)
                {
                    value = width.Value switch { "thin" => Pixels(1), "medium" => Pixels(3), "thick" => Pixels(5), _ => value };
                }
                string styleName = WidthStyleNames.GetOrAdd(property, static name => name[..^5] + "style");
                if (context.PropertyValue?.Invoke(styleName) is CssKeywordValue style && (style.Is("none") || style.Is("hidden"))) return Pixels(0);
                if (value is CssDimensionValue { Unit: "px" } border && definition.ComputedValue.Contains("snapped", StringComparison.OrdinalIgnoreCase) && context.DevicePixelRatio is double dpr && dpr > 0)
                {
                    double device = border.Value * dpr;
                    double snapped = device <= 0 ? 0 : device < 1 ? 1 / dpr : System.Math.Floor(device) / dpr;
                    return value.GetType() == typeof(CssDimensionValue) &&
                        BitConverter.DoubleToInt64Bits(snapped) == BitConverter.DoubleToInt64Bits(border.Value) ? value : Pixels(snapped);
                }
            }
            if (value is CssNumberValue { Value: 0 } && RangeFor(property, definition.Grammar, Values.Math.CssNumericDimension.Length).Found
                && !RangeFor(property, definition.Grammar, Values.Math.CssNumericDimension.Number).Found) return Pixels(0);
            return value;
        }

        private static CssValue ShadowLayers(CssValue value, string property, ComputedValueContext context) =>
            Layers(value, item => Shadow(item, property, context));


        private static CssValue Layers(CssValue value, Func<CssValue, CssValue> convert)
        {
            if (value is not CssListValue { Separator: CssListSeparator.Comma } list) return convert(value);
            CssValue[]? items = null;
            for (int i = 0; i < list.Items.Count; i++)
            {
                CssValue converted = convert(list.Items[i]);
                if (items == null && !ReferenceEquals(converted, list.Items[i]))
                {
                    items = new CssValue[list.Items.Count];
                    for (int j = 0; j < i; j++) items[j] = list.Items[j];
                }
                if (items != null) items[i] = converted;
            }
            return items == null ? value : new CssListValue(items, CssListSeparator.Comma);
        }
        private static CssValue FillSize(CssValue value)
        {
            var items = Items(value);
            if (items.Count == 2 && items[0] is CssKeywordValue { Value: "auto" } && items[1] is CssKeywordValue { Value: "auto" }) return items[0];
            return items.Count == 1 && value is not CssKeywordValue { Value: "auto" or "cover" or "contain" } ? Space(value, Keyword("auto")) : value;
        }
        private static CssValue RepeatPair(CssValue value)
        {
            var items = Items(value);
            if (items.Count != 2) return value;
            if (EqualValue(items[0], items[1])) return items[0];
            if (items[0] is CssKeywordValue a && items[1] is CssKeywordValue b)
            {
                if (a.Is("repeat") && b.Is("no-repeat")) return Keyword("repeat-x");
                if (a.Is("no-repeat") && b.Is("repeat")) return Keyword("repeat-y");
            }
            return value;
        }
        private static CssValue FourSides(CssValue value)
        {
            var items = Items(value);
            bool fill = items[items.Count - 1] is CssKeywordValue { Value: "fill" };
            int count = items.Count - (fill ? 1 : 0);
            if (count is < 2 or > 4) return value;
            if (count == 4 && EqualValue(items[1], items[3])) count--;
            if (count == 3 && EqualValue(items[0], items[2])) count--;
            if (count == 2 && EqualValue(items[0], items[1])) count--;
            if (count == items.Count - (fill ? 1 : 0)) return value;
            var compact = new CssValue[count + (fill ? 1 : 0)];
            for (int i = 0; i < count; i++) compact[i] = items[i];
            if (fill) compact[count] = Keyword("fill");
            return compact.Length == 1 ? compact[0] : new CssListValue(compact, CssListSeparator.Space);
        }
    }
}
