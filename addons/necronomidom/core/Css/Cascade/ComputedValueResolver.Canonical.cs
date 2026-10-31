
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue Canonical(string property, CssValue value, in ComputedValueContext context)
        {
            if (property is "background-position-x" or "background-position-y")
                return value is CssListValue { Separator: CssListSeparator.Comma }
                    ? PositionAxisLayers(value, context) : PositionAxis(value, context);
            if (property == "font-style" && value is CssListValue { Items.Count: 2 } style && IsZero(style.Items[1])) return Keyword("normal");
            if (property == "font-size-adjust") return FontSizeAdjust(value, context);
            if (property is "font-feature-settings" or "font-variation-settings") return FontSettings(value, property);
            if (property == "font-family") return Layers(value, FontFamily);
            if (property == "font-palette") return context.FontPaletteValue?.Invoke(value) ?? value;
            if (property is "animation-timing-function" or "transition-timing-function") return Layers(value, Easing);
            if (property == "position-area") return AnchorArea(value);
            if (property is "counter-increment" or "counter-reset" or "counter-set") return CssLayoutSpecifiedValues.Counters(value, property);
            if (property == "contain") return CssLayoutSpecifiedValues.Containment(value, true);
            if (property.StartsWith("align-", System.StringComparison.Ordinal) || property.StartsWith("justify-", System.StringComparison.Ordinal)) return Alignment(property, value, context);
            if (property == "aspect-ratio") return AspectRatio(value);
            if (property == "overflow-clip-margin") return OverflowClipMargin(value);
            if (property is "scroll-snap-align" or "scroll-snap-type") return ScrollSnap(value, property);
            if (property == "font-language-override" && value is CssStringValue language && language.Value.EndsWith(' ')) return new CssStringValue(language.Value.TrimEnd(' '));
            if (property == "animation-name") return Layers(value, AnimationName);
            if (property == "animation-duration") return AnimationDuration(value, context);
            if (property is "animation-range-start" or "animation-range-end")
                return value is CssListValue { Separator: CssListSeparator.Comma }
                    ? TimelineRangeLayers(value, property) : CssTimelineRangeValues.Normalize(property, value);
            if (property == "scale" && value is not CssKeywordValue { Value: "none" })
            {
                var items = Items(value);
                if (items.Count == 3 && items[2] is CssNumberValue { Value: 1 }) value = Space(items[0], items[1]);
                items = Items(value);
                if (items.Count == 2 && EqualValue(items[0], items[1])) return items[0];
            }
            if (property == "translate" && value is not CssKeywordValue { Value: "none" })
            {
                var items = Items(value);
                if (items.Count == 3 && IsZero(items[2])) value = Space(items[0], items[1]);
                items = Items(value);
                if (items.Count == 2 && IsZero(items[1])) return items[0];
            }
            if (property == "word-spacing" && value is CssKeywordValue { Value: "normal" }) return Pixels(0);
            if (property == "letter-spacing" && IsZero(value)) return Keyword("normal");
            if (property == "text-decoration-thickness" && value is CssKeywordValue thickness)
                return thickness.Value switch { "thin" => Pixels(1), "medium" => Pixels(3), "thick" => Pixels(5), _ => value };
            if (property.StartsWith("border-", System.StringComparison.Ordinal) && property.EndsWith("-radius", System.StringComparison.Ordinal)
                && value is CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } radius
                && EqualValue(radius.Items[0], radius.Items[1])) return radius.Items[0];
            return value;
        }

        // Capture the context/property only for an actual multi-layer branch, never for
        // every unrelated computed longhand that passes through Canonical.
        private static CssValue PositionAxisLayers(CssValue value, ComputedValueContext context) =>
            Layers(value, item => PositionAxis(item, context));
        private static CssValue TimelineRangeLayers(CssValue value, string property) =>
            Layers(value, item => CssTimelineRangeValues.Normalize(property, item));

        private static CssValue PositionAxis(CssValue value, ComputedValueContext context)
        {
            var items = Items(value);
            if (items.Count == 2 && items[0] is CssKeywordValue edge)
                return IsEndEdge(edge, context) ? EndOffset(items[1]) : items[1];
            return PositionKeyword(value, context);
        }

        private static CssValue FontSizeAdjust(CssValue value, ComputedValueContext context)
        {
            if (value is CssKeywordValue { Value: "none" }) return value;
            var items = Items(value);
            string metric = items.Count == 1 ? "ex-height" : items[0] is CssKeywordValue keyword ? keyword.Value : "ex-height";
            CssValue amount = items[items.Count - 1];
            if (amount is CssKeywordValue { Value: "from-font" })
            {
                double? basis = metric switch
                {
                    "ex-height" => context.Font.Ex, "cap-height" => context.Font.Cap,
                    "ch-width" => context.Font.Ch, "ic-width" => context.Font.Ic, "ic-height" => context.Font.IcHeight, _ => null
                };
                if (!basis.HasValue || context.Font.Size is not double size || size <= 0) return value;
                amount = new CssNumberValue(basis.Value / size, false);
            }
            return metric == "ex-height" ? amount : Space(Keyword(metric), amount);
        }

        private static CssValue Feature(CssValue value)
        {
            if (value is CssKeywordValue { Value: "normal" }) return value;
            var items = Items(value);
            if (items[0] is not CssStringValue) return value;
            if (items.Count == 1) return items[0];
            CssValue number = items[1];
            if (number is CssKeywordValue { Value: "on" or "off" } toggle)
            {
                if (toggle.Is("on")) return items[0];
                number = new CssIntegerValue(0);
            }
            return number is CssNumberValue { Value: 1 } ? items[0] : Space(items[0], number);
        }


        private static bool IsZero(CssValue value) => value is CssNumberValue { Value: 0 } or CssDimensionValue { Value: 0 };
        private static bool EqualValue(CssValue a, CssValue b) => ReferenceEquals(a, b) || (a, b) switch
        {
            (CssNumberValue x, CssNumberValue y) => x.Value == y.Value,
            (CssDimensionValue x, CssDimensionValue y) => x.Value == y.Value && x.Unit == y.Unit,
            (CssPercentageValue x, CssPercentageValue y) => x.Value == y.Value,
            (CssKeywordValue x, CssKeywordValue y) => x.Value == y.Value, _ => false
        };
    }
}
