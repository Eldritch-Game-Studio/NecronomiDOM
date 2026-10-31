using System.Collections.Generic;

namespace NecronomiDom.Css.Values
{
    // Fonts 4 and Counter Styles 3: omission of default productions is specified-value serialization, not computation.
    internal static class CssFontSpecifiedValues
    {
        internal static CssValue Normalize(string property, CssValue value)
        {
            if (property == "font-language-override" && value is CssStringValue language)
                return new CssStringValue(language.Value.TrimEnd(' '));
            if (property == "animation-name" && value is CssStringValue animation && animation.Value.Length != 0
                && !CssWideKeyword.IsWideKeyword(animation.Value)
                && !NecronomiDom.Dom.Names.AsciiEqual(animation.Value, "none") && !NecronomiDom.Dom.Names.AsciiEqual(animation.Value, "default"))
                return new CssKeywordValue(animation.Value);
            if (value is CssListValue { Separator: CssListSeparator.Space } list)
            {
                if (property == "font-size-adjust" && list.Items.Count == 2 && Is(list.Items[0], "ex-height")) return list.Items[1];
                if (property == "font-style" && list.Items.Count == 2 && Is(list.Items[0], "oblique") && list.Items[1] is CssDimensionValue { Value: 0 })
                    return new CssKeywordValue("normal");
            }
            if (value is CssFunctionValue { Name: "symbols", Arguments: CssListValue { Separator: CssListSeparator.Space } symbols }
                && symbols.Items.Count > 1 && Is(symbols.Items[0], "symbolic"))
            {
                var items = new List<CssValue>(symbols.Items.Count - 1);
                for (int i = 1; i < symbols.Items.Count; i++) items.Add(symbols.Items[i]);
                return new CssFunctionValue("symbols", items.Count == 1 ? items[0] : new CssListValue(items, CssListSeparator.Space));
            }
            return value;
        }
        private static bool Is(CssValue value, string keyword) => value is CssKeywordValue word && word.Is(keyword);
    }
}
