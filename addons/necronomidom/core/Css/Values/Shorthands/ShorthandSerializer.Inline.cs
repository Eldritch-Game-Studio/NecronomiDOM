using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.Values
{
    public static partial class ShorthandSerializer
    {
        private static string? VerticalAlign(Func<string, CssValue?> get)
        {
            string? source = Child("baseline-source", get), alignment = Child("alignment-baseline", get);
            CssValue? shift = get("baseline-shift");
            if (source == null || alignment == null || shift == null) return null;
            var parts = new List<string>(3);
            if (source != "auto") parts.Add(source);
            if (alignment != "baseline") parts.Add(alignment);
            if (!ZeroShift(shift)) parts.Add(shift.Serialize());
            return parts.Count == 0 ? "baseline" : string.Join(" ", parts);
        }

        private static bool ZeroShift(CssValue value) => value is CssNumberValue { Value: 0 }
            or CssDimensionValue { Value: 0 };

        private static string? Outline(Func<string, CssValue?> get)
        {
            var parts = new List<string>(3);
            foreach (string name in new[] { "outline-color", "outline-style", "outline-width" })
            {
                string? value = Child(name, get);
                if (value == null) return null;
                if (value != ShorthandExpansion.Initial(name)?.Serialize()) parts.Add(value);
            }
            return parts.Count == 0 ? Child("outline-style", get) : string.Join(" ", parts);
        }
    }
}
