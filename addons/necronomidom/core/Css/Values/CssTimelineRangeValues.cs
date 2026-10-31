namespace NecronomiDom.Css.Values
{
    // Scroll-driven Animations § 2.4: attachment range defaults and CSSOM serialization.
    internal static class CssTimelineRangeValues
    {
        internal static CssValue Normalize(string property, CssValue value)
        {
            if (property is not ("animation-range-start" or "animation-range-end")) return value;
            if (value is CssListValue { Separator: CssListSeparator.Space } list && list.Items.Count == 2
                && list.Items[0] is CssKeywordValue && list.Items[1] is CssPercentageValue percentage
                && percentage.Value == (property == "animation-range-start" ? 0 : 100)) return list.Items[0];
            return value;
        }

        internal static string Serialize(CssValue start, CssValue end)
        {
            start = Normalize("animation-range-start", start);
            end = Normalize("animation-range-end", end);
            CssKeywordValue? name = start as CssKeywordValue;
            if (name == null && start is CssListValue { Separator: CssListSeparator.Space } list && list.Items.Count > 0)
                name = list.Items[0] as CssKeywordValue;
            if (name != null && name.Is("normal")) name = null;
            bool implied = name == null
                ? end is CssKeywordValue keyword && keyword.Is("normal")
                    || start is not CssKeywordValue && end is CssPercentageValue { Value: 100 }
                : end is CssKeywordValue endName && name.Is(endName.Value);
            return implied ? start.Serialize() : start.Serialize() + " " + end.Serialize();
        }

        internal static bool EquivalentEnd(CssValue starts, CssValue expected, CssValue actual)
        {
            int count = LayerCount(starts);
            if (LayerCount(expected) != count || LayerCount(actual) != count) return false;
            for (int i = 0; i < count; i++)
            {
                CssValue start = Layer(starts, i), a = Layer(expected, i), b = Layer(actual, i);
                if (a.Serialize() == b.Serialize()) continue;
                bool numericStart = start is not CssKeywordValue
                    && start is not CssListValue { Separator: CssListSeparator.Space };
                if (!numericStart || !(Normal(a) && b is CssPercentageValue { Value: 100 }
                    || Normal(b) && a is CssPercentageValue { Value: 100 })) return false;
            }
            return true;
        }
        private static bool Normal(CssValue value) => value is CssKeywordValue keyword && keyword.Is("normal");
        private static int LayerCount(CssValue value) => value is CssListValue { Separator: CssListSeparator.Comma } list ? list.Items.Count : 1;
        private static CssValue Layer(CssValue value, int index) => value is CssListValue { Separator: CssListSeparator.Comma } list ? list.Items[index] : value;
    }
}
