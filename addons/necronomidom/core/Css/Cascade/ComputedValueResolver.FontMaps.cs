using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue FontSettings(CssValue value, string property)
        {
            if (value is CssKeywordValue { Value: "normal" }) return value;
            var map = new SortedDictionary<string, CssValue>(StringComparer.Ordinal);
            if (value is CssListValue { Separator: CssListSeparator.Comma } list)
                foreach (CssValue item in list.Items) AddSetting(map, item, property);
            else AddSetting(map, value, property);
            var output = new CssValue[map.Count];
            int index = 0;
            foreach (CssValue setting in map.Values) output[index++] = setting;
            return output.Length == 1 ? output[0] : new CssListValue(output, CssListSeparator.Comma);
        }

        private static void AddSetting(SortedDictionary<string, CssValue> map, CssValue value, string property)
        {
            var items = Items(value);
            if (items.Count == 0 || items[0] is not CssStringValue tag) return;
            CssValue computed = property == "font-feature-settings" ? Feature(value) : value;
            map[tag.Value] = computed;
        }

        private static CssValue FontFamily(CssValue value)
        {
            if (value is not CssStringValue family || family.Value.Length == 0) return value;
            IReadOnlyList<CssToken> tokens = CssTokenizer.Tokenize(family.Value);
            var identifiers = new List<CssValue>();
            foreach (CssToken token in tokens)
            {
                if (token.Kind is CssTokenKind.Whitespace or CssTokenKind.Eof) continue;
                if (token.Kind != CssTokenKind.Ident) return value;
                identifiers.Add(new CssKeywordValue(token.Value));
            }
            if (identifiers.Count == 0) return value;
            if (identifiers.Count == 1 && identifiers[0] is CssKeywordValue keyword &&
                (CssWideKeyword.IsWideKeyword(keyword.Value) || keyword.Value is "serif" or "sans-serif" or "cursive" or "fantasy" or "monospace" or "system-ui" or "emoji" or "math" or "fangsong" or "ui-serif" or "ui-sans-serif" or "ui-monospace" or "ui-rounded")) return value;
            return identifiers.Count == 1 ? identifiers[0] : new CssListValue(identifiers, CssListSeparator.Space);
        }
    }
}
