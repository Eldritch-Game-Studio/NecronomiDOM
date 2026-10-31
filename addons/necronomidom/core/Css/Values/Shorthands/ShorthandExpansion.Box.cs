using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;

namespace NecronomiDom.Css.Values
{
    internal static partial class ShorthandExpansion
    {
        private static bool Generic(PropertyDefinition definition, IReadOnlyList<ComponentValue> tokens, GrammarMatch match, Dictionary<string, CssValue> values)
        {
            string name = definition.Name;
            bool sides = name is "margin" or "padding" or "inset" or "scroll-margin" or "scroll-padding" or "border-width" or "border-style" or "border-color"
                || name.StartsWith("margin-", StringComparison.Ordinal) || name.StartsWith("padding-", StringComparison.Ordinal)
                || name.StartsWith("inset-", StringComparison.Ordinal) || name.StartsWith("scroll-margin-", StringComparison.Ordinal)
                || name.StartsWith("scroll-padding-", StringComparison.Ordinal)
                || name is "border-block-width" or "border-block-style" or "border-block-color" or "border-inline-width" or "border-inline-style" or "border-inline-color"
                or "overflow" or "overscroll-behavior";
            if (sides)
            {
                IReadOnlyList<CssValue> items = match.Value is CssListValue { Separator: CssListSeparator.Space } list ? list.Items : new[] { match.Value };
                for (int i = 0; i < definition.Longhands.Count; i++)
                    if (!Put(values, definition.Longhands[i], items[SideIndex(items.Count, i, definition.Longhands.Count)])) return false;
                return true;
            }
            bool any = false;
            foreach (string longhand in definition.Longhands)
            {
                CssValue? value = Capture(match, "'" + longhand + "'");
                if (value == null) continue;
                if (!Put(values, longhand, value)) return false;
                any = true;
            }
            if (name is "gap" or "place-content" or "place-items" or "place-self")
            {
                CssValue? first = Capture(match, "'" + definition.Longhands[0] + "'");
                if (first != null && Capture(match, "'" + definition.Longhands[1] + "'") == null)
                {
                    if (name == "place-content" && first.Serialize().Contains("baseline", StringComparison.Ordinal)) first = Keyword("start");
                    if (!Put(values, definition.Longhands[1], first)) return false;
                }
            }
            if (name == "list-style")
            {
                int none = 0;
                foreach (ComponentValue token in tokens) if (token is PreservedToken p && p.Token.IsIdent("none")) none++;
                if (none > 0)
                {
                    if (Capture(match, "'list-style-type'") == null || none == 2) values["list-style-type"] = Keyword("none");
                    if (Capture(match, "'list-style-image'") == null || none == 2) values["list-style-image"] = Keyword("none");
                }
            }
            if (name == "text-decoration-skip")
            {
                foreach (string leaf in Leaves(definition))
                    values[leaf] = match.Value.Serialize() == "none" ? Keyword("none") : Initial(leaf)!;
                return true;
            }
            return any;
        }

        private static int SideIndex(int count, int side, int total)
        {
            if (total == 2) return count == 1 ? 0 : side;
            return side switch { 0 => 0, 1 => count > 1 ? 1 : 0, 2 => count > 2 ? 2 : 0, _ => count > 3 ? 3 : count > 1 ? 1 : 0 };
        }

        private static bool Border(PropertyDefinition definition, GrammarMatch match, Dictionary<string, CssValue> values)
        {
            CssValue? width = Capture(match, "line-width");
            CssValue? style = Capture(match, "line-style");
            CssValue? color = Capture(match, "color");
            foreach (string leaf in Leaves(definition))
            {
                if (definition.Name == "border" && leaf.StartsWith("border-image-", StringComparison.Ordinal)) continue;
                CssValue? value = leaf.EndsWith("-width", StringComparison.Ordinal) ? width : leaf.EndsWith("-style", StringComparison.Ordinal) ? style
                    : leaf.EndsWith("-color", StringComparison.Ordinal) ? color : null;
                if (value != null) values[leaf] = value;
            }
            return true;
        }

        private static bool Radius(PropertyDefinition definition, IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            List<List<ComponentValue>> parts = Split(tokens, false);
            var horizontal = new List<CssValue>();
            var vertical = new List<CssValue>();
            foreach (ComponentValue token in parts[0])
            {
                if (!CssTypeParser.TryParse("length-percentage [0,∞]", new[] { token }, out CssValue? value) || value == null) return false;
                horizontal.Add(value);
            }
            if (parts.Count == 2)
                foreach (ComponentValue token in parts[1])
                {
                    if (!CssTypeParser.TryParse("length-percentage [0,∞]", new[] { token }, out CssValue? value) || value == null) return false;
                    vertical.Add(value);
                }
            else vertical.AddRange(horizontal);
            for (int i = 0; i < definition.Longhands.Count; i++)
            {
                CssValue h = horizontal[SideIndex(horizontal.Count, i, definition.Longhands.Count)];
                CssValue v = vertical[SideIndex(vertical.Count, i, definition.Longhands.Count)];
                values[definition.Longhands[i]] = h.Serialize() == v.Serialize() ? h : List(new[] { h, v });
            }
            return true;
        }

        private static bool Flex(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            values["flex-grow"] = new CssNumberValue(1, true);
            values["flex-shrink"] = new CssNumberValue(1, true);
            values["flex-basis"] = new CssPercentageValue(0);
            if (tokens.Count == 1 && tokens[0] is PreservedToken p && p.Token.IsIdent("none"))
            {
                values["flex-grow"] = new CssNumberValue(0, true);
                values["flex-shrink"] = new CssNumberValue(0, true);
                values["flex-basis"] = Keyword("auto");
                return true;
            }
            int number = 0;
            bool basis = false;
            foreach (ComponentValue token in tokens)
            {
                if (number < 2 && CssTypeParser.TryParse("number [0,∞]", new[] { token }, out CssValue? n) && n != null)
                    values[number++ == 0 ? "flex-grow" : "flex-shrink"] = n;
                else
                {
                    if (basis || !ParsePut(values, "flex-basis", new[] { token })) return false;
                    basis = true;
                }
            }
            return true;
        }
    }
}
