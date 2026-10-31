using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue Display(CssValue value, ComputedValueContext context)
        {
            bool blockify = context.IsRoot || context.IsFlexOrGridItem;
            if (context.PropertyValue?.Invoke("position") is CssKeywordValue { Value: "absolute" or "fixed" }) blockify = true;
            if (context.PropertyValue?.Invoke("float") is CssKeywordValue floating && !floating.Is("none")) blockify = true;
            return CssLayoutSpecifiedValues.Display(value, blockify);
        }

    }
}
