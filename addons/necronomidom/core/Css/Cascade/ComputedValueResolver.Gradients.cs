using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue GradientPrelude(CssValue prelude, string name, ComputedValueContext context)
        {
            if (name is "radial-gradient" or "repeating-radial-gradient") prelude = GradientRadii(prelude);
            if (prelude is not CssListValue { Separator: CssListSeparator.Space } list) return prelude;
            int at = -1, end = list.Items.Count;
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (list.Items[i] is not CssKeywordValue keyword) continue;
                if (keyword.Is("at")) at = i;
                else if (at >= 0 && keyword.Is("in")) { end = i; break; }
            }
            if (at < 0 || end <= at + 1) return prelude;
            if (end == at + 3 && list.Items[at + 1] is not CssKeywordValue && list.Items[at + 2] is not CssKeywordValue) return prelude;
            var coordinates = new CssValue[end - at - 1];
            for (int i = 0; i < coordinates.Length; i++) coordinates[i] = list.Items[at + 1 + i];
            CssValue position = Position(new CssListValue(coordinates, CssListSeparator.Space), "object-position", context);
            var components = Items(position);
            var result = new CssValue[at + 1 + components.Count + list.Items.Count - end];
            for (int i = 0; i <= at; i++) result[i] = list.Items[i];
            for (int i = 0; i < components.Count; i++) result[at + 1 + i] = components[i];
            for (int i = end; i < list.Items.Count; i++) result[at + 1 + components.Count + i - end] = list.Items[i];
            return new CssListValue(result, CssListSeparator.Space);
        }

        private static CssValue GradientRadii(CssValue value)
        {
            if (value is not CssListValue { Separator: CssListSeparator.Space } list) return GradientRadius(value);
            CssValue[]? changed = null;
            bool geometry = true;
            for (int i = 0; i < list.Items.Count; i++)
            {
                CssValue item = list.Items[i];
                if (item is CssKeywordValue { Value: "at" or "in" }) geometry = false;
                CssValue radius = geometry ? GradientRadius(item) : item;
                if (changed == null && !ReferenceEquals(radius, item))
                {
                    changed = new CssValue[list.Items.Count];
                    for (int j = 0; j < i; j++) changed[j] = list.Items[j];
                }
                if (changed != null) changed[i] = radius;
            }
            return changed == null ? value : new CssListValue(changed, list.Separator);
        }

        private static CssValue GradientRadius(CssValue value) => value switch
        {
            CssDimensionValue { Value: < 0 } dimension => new CssDimensionValue(0, dimension.Unit),
            CssPercentageValue { Value: < 0 } => new CssPercentageValue(0),
            _ => value
        };
    }
}
