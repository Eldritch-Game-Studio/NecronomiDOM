using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;

namespace NecronomiDom.Css.Values
{
    internal static partial class ShorthandExpansion
    {
        private static bool RangeLayers(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            var starts = new List<CssValue>();
            var ends = new List<CssValue>();
            foreach (List<ComponentValue> layer in Split(tokens, true))
            {
                GrammarMatch? match = new GrammarMatcher(layer, "animation-range").Match(GrammarParser.Parse("<'animation-range-start'> <'animation-range-end'>?"));
                if (match == null) return false;
                CssValue? start = Capture(match, "'animation-range-start'");
                if (start == null) return false;
                CssValue? end = Capture(match, "'animation-range-end'");
                if (end == null)
                {
                    CssValue first = start is CssListValue list && list.Items.Count > 0 ? list.Items[0] : start;
                    end = first is CssKeywordValue k && !k.Is("normal") ? List(new[] { first, new CssPercentageValue(100) }) : Keyword("normal");
                }
                starts.Add(CssTimelineRangeValues.Normalize("animation-range-start", start));
                ends.Add(CssTimelineRangeValues.Normalize("animation-range-end", end));
            }
            values["animation-range-start"] = List(starts, CssListSeparator.Comma);
            values["animation-range-end"] = List(ends, CssListSeparator.Comma);
            return true;
        }
    }
}
