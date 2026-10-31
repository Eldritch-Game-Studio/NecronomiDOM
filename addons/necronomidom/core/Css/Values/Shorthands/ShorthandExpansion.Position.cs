using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    internal static partial class ShorthandExpansion
    {
        private static bool PositionLayers(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            var xs = new List<CssValue>();
            var ys = new List<CssValue>();
            foreach (List<ComponentValue> layer in Split(tokens, true))
            {
                if (!Position(layer, out CssValue x, out CssValue y)) return false;
                xs.Add(x); ys.Add(y);
            }
            values["background-position-x"] = List(xs, CssListSeparator.Comma);
            values["background-position-y"] = List(ys, CssListSeparator.Comma);
            return true;
        }

        internal static bool Position(IReadOnlyList<ComponentValue> source, out CssValue x, out CssValue y)
        {
            source = TokenText.WithoutWhitespace(source);
            x = Keyword("center"); y = Keyword("center");
            if (source.Count == 0 || source.Count > 4) return false;
            var components = new List<CssValue>();
            foreach (ComponentValue token in source)
            {
                CssValue? value = token is PreservedToken { Token.Kind: CssTokenKind.Ident } p ? Keyword(NecronomiDom.Dom.Names.AsciiLower(p.Token.Value))
                    : CssTypeParser.TryParse("length-percentage", new[] { token }, out CssValue? length) ? length : null;
                if (value == null) return false;
                components.Add(value);
            }
            if (components.Count == 1)
            {
                if (Vertical(components[0])) y = components[0]; else x = components[0];
                return true;
            }
            if (components.Count == 2)
            {
                if (Vertical(components[0]) || Horizontal(components[1])) { x = components[1]; y = components[0]; }
                else { x = components[0]; y = components[1]; }
                return true;
            }
            bool horizontal = false, vertical = false;
            for (int i = 0; i < components.Count; i++)
            {
                CssValue edge = components[i];
                bool h = Horizontal(edge), v = Vertical(edge);
                if (h || v)
                {
                    CssValue axis = edge;
                    if (i + 1 < components.Count && components[i + 1] is not CssKeywordValue) axis = List(new[] { edge, components[++i] });
                    if (h) { if (horizontal) return false; x = axis; horizontal = true; }
                    else { if (vertical) return false; y = axis; vertical = true; }
                }
                else if (edge is CssKeywordValue k && k.Is("center"))
                {
                    bool laterHorizontal = i + 1 < components.Count && Horizontal(components[i + 1]);
                    if (!horizontal && !laterHorizontal) { x = edge; horizontal = true; }
                    else if (!vertical) { y = edge; vertical = true; }
                    else return false;
                }
                else return false;
            }
            return horizontal && vertical;
        }
        private static bool Horizontal(CssValue value) => value is CssKeywordValue k && (k.Is("left") || k.Is("right"));
        private static bool Vertical(CssValue value) => value is CssKeywordValue k && (k.Is("top") || k.Is("bottom"));
    }
}
