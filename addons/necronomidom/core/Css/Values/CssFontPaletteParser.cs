using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Css.Values
{
    /// <summary>CSS Fonts 4 font-palette selectors and recursive palette-mix() grammar.</summary>
    public static class CssFontPaletteParser
    {
        public static bool TryParse(FunctionValue function, out CssValue? value)
        {
            value = null;
            if (!NecronomiDom.Dom.Names.AsciiEqual(function.Name, "palette-mix")) return false;
            List<ComponentValue> tokens = TokenText.WithoutWhitespace(function.Arguments);
            var parts = new List<List<ComponentValue>>();
            var part = new List<ComponentValue>();
            foreach (ComponentValue token in tokens)
            {
                if (token is PreservedToken { Token.Kind: CssTokenKind.Comma })
                {
                    if (part.Count == 0) return false;
                    parts.Add(part);
                    part = new List<ComponentValue>();
                }
                else part.Add(token);
            }
            if (part.Count == 0) return false;
            parts.Add(part);
            string space = "oklab", hue = "shorter";
            int start = 0;
            if (parts[0][0] is PreservedToken first && first.Token.IsIdent("in"))
            {
                if (!CssColorParser.TryInterpolation(parts[0], out space, out hue)) return false;
                start = 1;
            }
            if (start == parts.Count) return false;
            var items = new CssPaletteMixItem[parts.Count - start];
            for (int i = start; i < parts.Count; i++)
            {
                if (!TryItem(parts[i], out CssPaletteMixItem item)) return false;
                items[i - start] = item;
            }
            value = new CssPaletteMixValue(space, hue, items);
            return true;
        }

        private static bool TryItem(IReadOnlyList<ComponentValue> tokens, out CssPaletteMixItem item)
        {
            item = default;
            if (tokens.Count is not (1 or 2)) return false;
            int selectorIndex = 0;
            CssValue? percentage = null;
            if (tokens.Count == 2)
            {
                int percentageIndex = CssTypeParser.Primitive("percentage", tokens[0], 0, 100, out percentage) ? 0 : 1;
                if (percentageIndex == 1 && !CssTypeParser.Primitive("percentage", tokens[1], 0, 100, out percentage)) return false;
                selectorIndex = 1 - percentageIndex;
            }
            if (!TrySelector(tokens[selectorIndex], out CssValue? selector)) return false;
            item = new CssPaletteMixItem(selector!, percentage);
            return true;
        }

        private static bool TrySelector(ComponentValue token, out CssValue? value)
        {
            value = null;
            if (token is FunctionValue function) return TryParse(function, out value);
            if (token is not PreservedToken { Token.Kind: CssTokenKind.Ident } ident) return false;
            string name = ident.Token.Value;
            if (name.StartsWith("--", StringComparison.Ordinal)) value = new CssKeywordValue(name);
            else if (ident.Token.IsIdent("normal") || ident.Token.IsIdent("light") || ident.Token.IsIdent("dark"))
                value = new CssKeywordValue(NecronomiDom.Dom.Names.AsciiLower(name));
            return value != null;
        }
    }
}
