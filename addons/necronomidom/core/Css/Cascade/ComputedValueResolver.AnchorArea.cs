using System;

using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue AnchorArea(CssValue value)
        {
            var items = Items(value);
            if (items.Count != 2 || items[0] is not CssKeywordValue first || items[1] is not CssKeywordValue second) return value;
            string a = first.Value, b = second.Value;
            int firstAxis = AreaAxis(a), secondAxis = AreaAxis(b);
            if (a == b) return first;
            if (b == "span-all" && firstAxis == 0 && AmbiguousArea(a)) return Keyword(AxisArea(a, "block-"));
            if (a == "span-all" && secondAxis == 0 && AmbiguousArea(b)) return Keyword(AxisArea(b, "inline-"));
            if (a == "span-all" && secondAxis != 0) return second;
            if (b == "span-all" && firstAxis != 0) return first;
            bool logical = a.Contains("block-", StringComparison.Ordinal) || a.Contains("inline-", StringComparison.Ordinal)
                || b.Contains("block-", StringComparison.Ordinal) || b.Contains("inline-", StringComparison.Ordinal);
            if (firstAxis == 2 || secondAxis == 1) (a, b) = (b, a);
            if (logical) { a = ShortArea(a); b = ShortArea(b); }
            if (a == b) return Keyword(a);
            return a == first.Value && b == second.Value ? value : Space(Keyword(a), Keyword(b));
        }

        private static int AreaAxis(string value)
        {
            if (value.Contains("block-", StringComparison.Ordinal)) return 1;
            if (value.Contains("inline-", StringComparison.Ordinal)) return 2;
            if (value.Contains("left", StringComparison.Ordinal) || value.Contains("right", StringComparison.Ordinal) || value.Contains("x-", StringComparison.Ordinal)) return 1;
            if (value.Contains("top", StringComparison.Ordinal) || value.Contains("bottom", StringComparison.Ordinal) || value.Contains("y-", StringComparison.Ordinal)) return 2;
            return 0;
        }
        private static string ShortArea(string value) => value.Replace("block-", "", StringComparison.Ordinal).Replace("inline-", "", StringComparison.Ordinal);
        private static bool AmbiguousArea(string value) =>
            value.EndsWith("start", StringComparison.Ordinal) || value.EndsWith("end", StringComparison.Ordinal);
        private static string AxisArea(string value, string axis)
        {
            int direction = value.Length - (value.EndsWith("start", StringComparison.Ordinal) ? 5 : 3);
            return string.Concat(value.AsSpan(0, direction), axis.AsSpan(), value.AsSpan(direction));
        }
    }
}
