
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue Position(CssValue value, string property, ComputedValueContext context)
        {
            // Like Blink's computed mask layer list, positions repeat/truncate to mask-image.
            if (property == "mask-position" && context.PropertyValue?.Invoke("mask-image") is CssValue images)
            {
                int imageCount = images is CssListValue { Separator: CssListSeparator.Comma } imageLayers ? imageLayers.Items.Count : 1;
                var source = value as CssListValue;
                if (source?.Separator != CssListSeparator.Comma) source = null;
                int sourceCount = source?.Items.Count ?? 1;
                if (sourceCount == 0 || imageCount == 0) return value;
                if (imageCount == 1) return Position(source == null ? value : source.Items[0], "", context);
                CssValue[]? matched = source == null || sourceCount != imageCount ? new CssValue[imageCount] : null;
                for (int i = 0; i < imageCount; i++)
                {
                    CssValue item = source == null ? value : source.Items[i % sourceCount];
                    CssValue position = Position(item, "", context);
                    if (matched == null && !ReferenceEquals(position, item))
                    {
                        matched = new CssValue[imageCount];
                        for (int j = 0; j < i; j++) matched[j] = source!.Items[j];
                    }
                    if (matched != null) matched[i] = position;
                }
                return matched == null ? value : new CssListValue(matched, CssListSeparator.Comma);
            }
            if (value is CssListValue { Separator: CssListSeparator.Comma } layers)
            {
                CssValue[]? positions = null;
                for (int i = 0; i < layers.Items.Count; i++)
                {
                    CssValue position = Position(layers.Items[i], property, context);
                    if (positions == null && !ReferenceEquals(position, layers.Items[i]))
                    {
                        positions = new CssValue[layers.Items.Count];
                        for (int j = 0; j < i; j++) positions[j] = layers.Items[j];
                    }
                    if (positions != null) positions[i] = position;
                }
                return positions == null ? value : new CssListValue(positions, CssListSeparator.Comma);
            }
            var items = Items(value);
            if (items.Count == 0) return value;
            CssValue? x = null, y = null, z = null;
            int count = items.Count;
            if (property == "transform-origin" && count == 3) { z = items[2]; count = 2; }
            if (count >= 3)
            {
                for (int i = 0; i < count; i++)
                {
                    if (items[i] is not CssKeywordValue edge) return value;
                    CssValue offset = Pixels(0);
                    if (i + 1 < count && items[i + 1] is not CssKeywordValue) offset = items[++i];
                    CssValue coordinate = edge.Value switch
                    {
                        "left" or "top" or "right" or "bottom" or "x-start" or "x-end" or "y-start" or "y-end"
                            => IsEndEdge(edge, context) ? EndOffset(offset) : offset,
                        "center" => new CssPercentageValue(50),
                        _ => edge
                    };
                    if (edge.Value is "top" or "bottom" or "y-start" or "y-end") y = coordinate;
                    else if (edge.Value is "left" or "right" or "x-start" or "x-end") x = coordinate;
                    else if (x == null) x = coordinate;
                    else y = coordinate;
                }
            }
            else if (count == 1)
            {
                if (items[0] is CssKeywordValue { Value: "top" or "bottom" or "y-start" or "y-end" }) { x = new CssPercentageValue(50); y = PositionKeyword(items[0], context); }
                else { x = PositionKeyword(items[0], context); y = new CssPercentageValue(50); }
            }
            else
            {
                bool reversed = items[0] is CssKeywordValue { Value: "top" or "bottom" or "y-start" or "y-end" }
                    || items[1] is CssKeywordValue { Value: "left" or "right" or "x-start" or "x-end" };
                x = PositionKeyword(items[reversed ? 1 : 0], context); y = PositionKeyword(items[reversed ? 0 : 1], context);
            }
            x ??= new CssPercentageValue(50); y ??= new CssPercentageValue(50);
            if (z is CssDimensionValue { Value: 0 } or CssNumberValue { Value: 0 }) z = null;
            // Normalize already preserves unchanged children. Keep their list too: no copied
            // coordinate buffer, and both CSSOM projections can retain the same declaration.
            // This is structural sharing, not a cached result; live slots are read on every call.
            if (value is CssListValue { Separator: CssListSeparator.Space } physical &&
                physical.Items.Count == (z == null ? 2 : 3) &&
                ReferenceEquals(physical.Items[0], x) && ReferenceEquals(physical.Items[1], y) &&
                (z == null || ReferenceEquals(physical.Items[2], z))) return value;
            return z == null ? Space(x, y) : Space(x, y, z);
        }

        private static CssValue PositionKeyword(CssValue value, ComputedValueContext context)
        {
            // Background/mask shorthands retain edge-offset pairs as one axis value.
            if (value is CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } axis
                && axis.Items[0] is CssKeywordValue edge)
                return IsEndEdge(edge, context) ? EndOffset(axis.Items[1]) : axis.Items[1];
            if (value is not CssKeywordValue keyword) return value;
            return keyword.Value switch
            {
                "left" or "top" => new CssPercentageValue(0),
                "center" => new CssPercentageValue(50),
                "right" or "bottom" => new CssPercentageValue(100),
                "x-start" or "x-end" or "y-start" or "y-end" => new CssPercentageValue(IsEndEdge(keyword, context) ? 100 : 0),
                _ => value
            };
        }

        private static bool IsRtl(ComputedValueContext context) => context.PropertyValue?.Invoke("direction") is CssKeywordValue { Value: "rtl" };
        private static bool IsEndEdge(CssKeywordValue edge, ComputedValueContext context) => edge.Value switch
        {
            "right" or "bottom" => true,
            "x-start" => LogicalXStartIsEnd(context), "x-end" => !LogicalXStartIsEnd(context),
            "y-start" => LogicalYStartIsEnd(context), "y-end" => !LogicalYStartIsEnd(context), _ => false
        };
        private static string WritingMode(ComputedValueContext context) =>
            (context.PropertyValue?.Invoke("writing-mode") as CssKeywordValue)?.Value ?? "horizontal-tb";
        private static bool LogicalXStartIsEnd(ComputedValueContext context) => WritingMode(context) switch
        {
            "vertical-rl" or "sideways-rl" => true,
            "vertical-lr" or "sideways-lr" => false,
            _ => IsRtl(context)
        };
        private static bool LogicalYStartIsEnd(ComputedValueContext context) => WritingMode(context) switch
        {
            "sideways-lr" => !IsRtl(context),
            "vertical-rl" or "vertical-lr" or "sideways-rl" => IsRtl(context),
            _ => false
        };

        private static CssValue EndOffset(CssValue offset)
        {
            if (offset is CssPercentageValue percent) return new CssPercentageValue(100 - percent.Value);
            if (offset is CssDimensionValue { Value: 0 } or CssNumberValue { Value: 0 }) return new CssPercentageValue(100);
            CssNumericType percentageType = CssNumericType.For(CssNumericDimension.Percentage);
            CssNumericType lengthType = CssNumericType.For(CssNumericDimension.Length);
            CssNumericType combined = lengthType with { PercentageHint = CssNumericDimension.Length };
            CssMathExpression expression;
            string operation = "-";
            if (offset is CssDimensionValue dimension)
            {
                operation = dimension.Value < 0 ? "+" : "-";
                expression = new CssMathLiteral(System.Math.Abs(dimension.Value), dimension.Unit, lengthType);
            }
            else if (offset is CssMathValue math) expression = math.Root;
            else return offset;
            return new CssMathValue(new CssMathOperation(operation, new CssMathExpression[]
            {
                new CssMathLiteral(100, "%", percentageType), expression
            }, combined), combined).Simplify(CssMathEvaluationContext.Specified);
        }
    }
}
