using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssPaletteMixValue NormalizePaletteMix(CssPaletteMixValue mix, string property, ComputedValueContext context)
        {
            CssPaletteMixItem[]? changed = null;
            for (int i = 0; i < mix.Items.Count; i++)
            {
                CssPaletteMixItem item = mix.Items[i];
                CssValue palette = Normalize(item.Palette, property, context);
                CssValue? percentage = item.Percentage == null ? null : Normalize(item.Percentage, property, context);
                if (changed == null && (!ReferenceEquals(palette, item.Palette) || !ReferenceEquals(percentage, item.Percentage)))
                {
                    changed = new CssPaletteMixItem[mix.Items.Count];
                    for (int j = 0; j < i; j++) changed[j] = mix.Items[j];
                }
                if (changed != null) changed[i] = new CssPaletteMixItem(palette, percentage);
            }
            // Resource equivalence is intentionally left to ComputedValueContext.FontPaletteValue.
            // No font palette contents can be inferred from the selector alone.
            return changed == null ? mix : new CssPaletteMixValue(mix.Space, mix.HueMethod, changed);
        }
    }
}
