
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        // CSS Shapes 1 § 3 and Shapes 2 § 5: basic shapes compute lengths and rect/xywh to inset.
        private static CssValue ShapeFunction(CssFunctionValue function, CssValue arguments, string property, ComputedValueContext context)
        {
            string name = function.Name;
            if (name is "circle" or "ellipse") arguments = GradientRadii(arguments);
            if (name == "shape") arguments = ShapeArcRadii(arguments);
            if (name is "inset" or "xywh" or "rect" && property != "clip") arguments = ShapeCornerRadii(arguments);
            if (name is "xywh" or "rect" && property != "clip" && TryShapeInsets(arguments, name == "xywh", property, context, out CssValue insets))
            {
                name = "inset";
                arguments = insets;
            }
            CssValue? canonical = property == "clip" && name == "rect" ? arguments : CssShapeSpecifiedValues.FunctionArguments(name, arguments);
            return name == function.Name && ReferenceEquals(canonical, function.Arguments) ? function : new CssFunctionValue(name, canonical);
        }

        private static CssValue ShapeCornerRadii(CssValue value)
        {
            if (value is CssListValue { Separator: CssListSeparator.Comma }) return value;
            CssListValue? slash = value as CssListValue;
            if (slash?.Separator != CssListSeparator.Slash) slash = null;
            CssValue prefix = slash == null ? value : slash.Items[0];
            var items = Items(prefix);
            CssValue[]? changed = null;
            bool radii = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is CssKeywordValue { Value: "round" }) radii = true;
                CssValue item = radii ? GradientRadius(items[i]) : items[i];
                if (changed == null && !ReferenceEquals(item, items[i]))
                {
                    changed = new CssValue[items.Count];
                    for (int j = 0; j < i; j++) changed[j] = items[j];
                }
                if (changed != null) changed[i] = item;
            }
            CssValue first = changed == null ? prefix : new CssListValue(changed, CssListSeparator.Space);
            if (slash == null) return first;
            CssValue second = GradientRadii(slash.Items[1]);
            return ReferenceEquals(first, prefix) && ReferenceEquals(second, slash.Items[1]) ? value
                : new CssListValue(new[] { first, second }, CssListSeparator.Slash);
        }

        private static bool TryShapeInsets(CssValue arguments, bool xywh, string property, ComputedValueContext context, out CssValue result)
        {
            result = arguments;
            if (arguments is CssListValue { Separator: CssListSeparator.Comma }) return false;
            CssListValue? slash = arguments as CssListValue;
            if (slash?.Separator != CssListSeparator.Slash) slash = null;
            var items = Items(slash == null ? arguments : slash.Items[0]);
            if (items.Count < 4) return false;
            CssValue top, right, bottom, left;
            if (xywh)
            {
                if (!TryShapeSize(items[2], out CssValue width) || !TryShapeSize(items[3], out CssValue height)
                    || !TryOffsetSum(items[0], width, property, context, out CssValue horizontal)
                    || !TryOffsetSum(items[1], height, property, context, out CssValue vertical)) return false;
                top = items[1]; left = items[0];
                right = ComputedEndOffset(horizontal, property, context);
                bottom = ComputedEndOffset(vertical, property, context);
            }
            else
            {
                top = AutomaticShapeEdge(items[0]); left = AutomaticShapeEdge(items[3]);
                if (!IsShapeOffset(items[1]) && items[1] is not CssKeywordValue { Value: "auto" }
                    || !IsShapeOffset(items[2]) && items[2] is not CssKeywordValue { Value: "auto" }) return false;
                right = items[1] is CssKeywordValue { Value: "auto" } ? Pixels(0) : ComputedEndOffset(items[1], property, context);
                bottom = items[2] is CssKeywordValue { Value: "auto" } ? Pixels(0) : ComputedEndOffset(items[2], property, context);
            }
            var coordinates = new CssValue[items.Count];
            coordinates[0] = top; coordinates[1] = right; coordinates[2] = bottom; coordinates[3] = left;
            for (int i = 4; i < coordinates.Length; i++) coordinates[i] = items[i];
            result = new CssListValue(coordinates, CssListSeparator.Space);
            if (slash != null) result = new CssListValue(new[] { result, slash.Items[1] }, CssListSeparator.Slash);
            return true;
        }

        private static CssValue AutomaticShapeEdge(CssValue value) => value is CssKeywordValue { Value: "auto" } ? Pixels(0) : value;
        private static CssValue ComputedEndOffset(CssValue value, string property, ComputedValueContext context)
        {
            CssValue result = EndOffset(value);
            return result is CssMathValue math ? MathValue(math, property, context) : result;
        }

        private static bool TryOffsetSum(CssValue first, CssValue second, string property, ComputedValueContext context, out CssValue result)
        {
            result = first;
            if (!IsShapeOffset(first) || !IsShapeOffset(second)) return false;
            if (first is CssPercentageValue a && second is CssPercentageValue b) { result = new CssPercentageValue(a.Value + b.Value); return true; }
            if (first is CssDimensionValue x && second is CssDimensionValue y && x.Unit == y.Unit)
            { result = new CssDimensionValue(x.Value + y.Value, x.Unit); return true; }
            CssMathExpression? left = OffsetExpression(first), right = OffsetExpression(second);
            if (left == null || right == null) return false;
            CssNumericType type = CssNumericType.For(CssNumericDimension.Length) with { PercentageHint = CssNumericDimension.Length };
            result = MathValue(new CssMathValue(new CssMathOperation("+", new CssMathExpression[] { left, right }, type), type), property, context);
            return true;
        }

        private static bool IsShapeOffset(CssValue value) => value switch
        {
            CssMathValue math => math.Fits("length-percentage", CssNumericDimension.Length),
            CssDimensionValue dimension => CssMathUnits.TryGet(dimension.Unit, out CssNumericDimension type, out _) && type == CssNumericDimension.Length,
            CssPercentageValue => true,
            CssNumberValue { Value: 0 } => true,
            _ => false
        };

        private static CssMathExpression? OffsetExpression(CssValue value) => value switch
        {
            CssMathValue math => math.Root,
            CssDimensionValue dimension => new CssMathLiteral(dimension.Value, dimension.Unit, CssNumericType.For(CssNumericDimension.Length)),
            CssPercentageValue percentage => new CssMathLiteral(percentage.Value, "%", CssNumericType.For(CssNumericDimension.Percentage)),
            CssNumberValue { Value: 0 } => new CssMathLiteral(0, "px", CssNumericType.For(CssNumericDimension.Length)),
            _ => null
        };
    }
}
