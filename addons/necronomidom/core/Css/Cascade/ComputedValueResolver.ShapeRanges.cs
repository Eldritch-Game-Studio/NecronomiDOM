using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        // Do not flatten xywh when its nonnegative size clamp still requires an unavailable base.
        private static bool TryShapeSize(CssValue value, out CssValue result)
        {
            result = GradientRadius(value);
            return result is not CssMathValue math || NonnegativeExpression(math.Root);
        }

        private static bool NonnegativeExpression(CssMathExpression expression)
        {
            if (expression is CssMathLiteral literal) return double.IsFinite(literal.Value) && literal.Value >= 0;
            if (expression is not CssMathOperation operation) return false;
            if (operation.Name is not ("+" or "*" or "min" or "max" or "abs" or "hypot" or "sqrt")) return false;
            foreach (CssMathExpression argument in operation.Arguments)
                if (!NonnegativeExpression(argument)) return false;
            return true;
        }

        private static CssValue ShapeArcRadii(CssValue value)
        {
            if (value is not CssListValue { Separator: CssListSeparator.Comma } groups) return value;
            CssValue[]? changedGroups = null;
            for (int i = 0; i < groups.Items.Count; i++)
            {
                CssValue group = groups.Items[i];
                if (group is CssListValue { Separator: CssListSeparator.Space } list && list.Items[0] is CssKeywordValue { Value: "arc" })
                {
                    CssValue[]? changed = null;
                    bool radii = false;
                    for (int j = 1; j < list.Items.Count; j++)
                    {
                        CssValue item = list.Items[j];
                        if (item is CssKeywordValue keyword) radii = keyword.Is("of");
                        CssValue radius = radii ? GradientRadius(item) : item;
                        if (changed == null && !ReferenceEquals(radius, item))
                        {
                            changed = new CssValue[list.Items.Count];
                            for (int k = 0; k < j; k++) changed[k] = list.Items[k];
                        }
                        if (changed != null) changed[j] = radius;
                    }
                    if (changed != null) group = new CssListValue(changed, list.Separator);
                }
                if (changedGroups == null && !ReferenceEquals(group, groups.Items[i]))
                {
                    changedGroups = new CssValue[groups.Items.Count];
                    for (int j = 0; j < i; j++) changedGroups[j] = groups.Items[j];
                }
                if (changedGroups != null) changedGroups[i] = group;
            }
            return changedGroups == null ? value : new CssListValue(changedGroups, groups.Separator);
        }
    }
}
