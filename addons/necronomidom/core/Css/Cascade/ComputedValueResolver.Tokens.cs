using System;

using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue Alignment(string property, CssValue value, ComputedValueContext context)
        {
            var items = Items(value);
            if (items.Count == 2 && items[0] is CssKeywordValue { Value: "first" } && items[1] is CssKeywordValue { Value: "baseline" }) return items[1];
            if (items.Count != 0 && items[items.Count - 1] is CssKeywordValue { Value: "flex-start" or "flex-end" } edge)
            {
                CssValue flow = Keyword(edge.Value == "flex-start" ? "flow-start" : "flow-end");
                return items.Count == 1 ? flow : Space(items[0], flow);
            }
            if (property == "justify-items" && value is CssKeywordValue { Value: "legacy" })
            {
                CssValue? inherited = context.ParentPropertyValue?.Invoke("justify-items");
                if (inherited is CssListValue parent)
                    foreach (CssValue item in parent.Items)
                        if (item is CssKeywordValue { Value: "legacy" }) return inherited;
                return Keyword("normal");
            }
            return value;
        }

        private static CssValue AnimationName(CssValue value)
        {
            if (value is not CssStringValue name || name.Value.Length == 0 ||
                CssWideKeyword.IsWideKeyword(name.Value) || name.Value.Equals("none", StringComparison.OrdinalIgnoreCase)
                || name.Value.Equals("default", StringComparison.OrdinalIgnoreCase)) return value;
            return new CssKeywordValue(name.Value);
        }


        private static CssValue AnimationDuration(CssValue value, ComputedValueContext context)
        {
            CssValue? timeline = context.PropertyValue?.Invoke("animation-timeline");
            if (timeline == null) return value;
            var timelines = timeline is CssListValue { Separator: CssListSeparator.Comma } timelineList ? new ValueItems(timelineList.Items) : Items(timeline);
            if (value is not CssListValue { Separator: CssListSeparator.Comma } list)
                return value is CssKeywordValue { Value: "auto" } && timelines[0] is CssKeywordValue { Value: "auto" } ? new CssDimensionValue(0, "s") : value;
            CssValue[]? changed = null;
            for (int i = 0; i < list.Items.Count; i++)
            {
                CssValue item = list.Items[i];
                if (item is CssKeywordValue { Value: "auto" } && timelines[i % timelines.Count] is CssKeywordValue { Value: "auto" }) item = new CssDimensionValue(0, "s");
                if (changed == null && !ReferenceEquals(item, list.Items[i]))
                {
                    changed = new CssValue[list.Items.Count];
                    for (int j = 0; j < i; j++) changed[j] = list.Items[j];
                }
                if (changed != null) changed[i] = item;
            }
            return changed == null ? value : new CssListValue(changed, CssListSeparator.Comma);
        }

        private static CssValue AspectRatio(CssValue value)
        {
            if (value is CssNumberValue or Values.Math.CssMathValue)
                return new CssListValue(new[] { value, new CssNumberValue(1, true) }, CssListSeparator.Slash);
            if (value is CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } list && list.Items[0] is CssKeywordValue { Value: "auto" })
            {
                CssValue ratio = AspectRatio(list.Items[1]);
                return ReferenceEquals(ratio, list.Items[1]) ? value : Space(list.Items[0], ratio);
            }
            return value;
        }

        private static CssValue OverflowClipMargin(CssValue value)
        {
            var items = Items(value);
            CssKeywordValue? box = null;
            CssValue? length = null;
            for (int i = 0; i < items.Count; i++)
            {
                CssValue item = items[i];
                if (item is CssKeywordValue keyword) box = keyword;
                else length = item;
            }
            if (box == null) return value;
            if (box.Is("padding-box")) return length ?? Pixels(0);
            return length == null || IsZero(length) ? box : Space(box, length);
        }

        private static CssValue ScrollSnap(CssValue value, string property)
        {
            var items = Items(value);
            if (items.Count == 2 && (property == "scroll-snap-align" ? EqualValue(items[0], items[1]) : items[1] is CssKeywordValue { Value: "proximity" })) return items[0];
            return value;
        }
    }
}
