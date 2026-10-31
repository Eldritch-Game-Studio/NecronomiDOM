using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values
{
    /// <summary>Specified component-value conversion (CSSOM § 6.7); property validity belongs to CssPropertyParser.</summary>
    public static class CssValueParser
    {
        public static CssValue? Parse(IReadOnlyList<ComponentValue> tokens)
        {
            tokens = TokenText.WithoutWhitespace(tokens);
            if (tokens.Count == 0) return new CssListValue(Array.Empty<CssValue>(), CssListSeparator.Space);
            if (CssColorParser.TryParse(tokens, out CssValue? color)) return color;
            var groups = new List<CssValue>();
            var part = new List<ComponentValue>();
            foreach (ComponentValue token in tokens)
            {
                if (token is PreservedToken { Token.Kind: CssTokenKind.Comma })
                {
                    CssValue? parsed = Space(part);
                    if (parsed == null) return null;
                    groups.Add(parsed);
                    part.Clear();
                }
                else part.Add(token);
            }
            CssValue? last = Space(part);
            if (last == null) return null;
            groups.Add(last);
            return groups.Count == 1 ? last : new CssListValue(groups, CssListSeparator.Comma);
        }

        private static CssValue? Space(IReadOnlyList<ComponentValue> tokens)
        {
            var items = new List<CssValue>(tokens.Count);
            foreach (ComponentValue component in tokens)
            {
                CssValue? value = Convert(component);
                if (value == null) return null;
                items.Add(value);
            }
            return items.Count == 1 ? items[0] : new CssListValue(items, CssListSeparator.Space);
        }

        private static CssValue? Convert(ComponentValue component)
        {
            if (component is FunctionValue f)
            {
                if (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "calc-size")) return CssMathParser.TryParseSizing(f, out CssValue? sizing) ? sizing : null;
                if (CssMathParser.IsMathFunction(f.Name)) return CssMathParser.TryParse(f, out CssValue? math) ? math : null;
                if (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "palette-mix")) return CssFontPaletteParser.TryParse(f, out CssValue? palette) ? palette : null;
                bool colorFunction = CssColorParser.IsColorFunction(f.Name), imageFunction = CssImageParser.IsImageFunction(f.Name);
                if (colorFunction || imageFunction)
                {
                    if (colorFunction && CssColorParser.TryParse(component, out CssValue? color)) return color;
                    if (imageFunction && CssImageParser.TryParse(component, out CssValue? image)) return image;
                    return null;
                }
                if (NecronomiDom.Dom.Names.AsciiEqual(f.Name, "attr"))
                    return CssAttrParser.TryParse(f, out CssValue? attr) ? attr : null;
                CssValue? args = Parse(f.Arguments);
                return args == null ? null : new CssFunctionValue(CanonicalFunction(f.Name), args);
            }
            if (component is SimpleBlock { Opener: CssTokenKind.LeftBracket or CssTokenKind.LeftParen } block)
            {
                CssValue? content = Parse(block.Value);
                return content == null ? null : new CssBlockValue(content, block.Opener == CssTokenKind.LeftParen ? '(' : '[');
            }
            if (component is not PreservedToken preserved) return null;
            CssToken t = preserved.Token;
            return t.Kind switch
            {
                CssTokenKind.Ident => new CssKeywordValue(t.Value),
                CssTokenKind.String => new CssStringValue(t.Value),
                CssTokenKind.Url => new CssUrlValue(t.Value),
                CssTokenKind.Number => new CssNumberValue(CssNumericRange.Literal(t.Number), t.NumberType == CssNumberType.Integer),
                CssTokenKind.Dimension => new CssDimensionValue(CssNumericRange.Literal(t.Number), NecronomiDom.Dom.Names.AsciiLower(t.Unit)),
                CssTokenKind.Percentage => new CssPercentageValue(CssNumericRange.Literal(t.Number)),
                CssTokenKind.Delim => new CssDelimValue(t.Value),
                CssTokenKind.Colon => new CssDelimValue(":"),
                CssTokenKind.Hash => CssColorParser.TryParse(component, out CssValue? c) ? c : new CssDelimValue("#" + t.Value),
                _ => null,
            };
        }

        internal static string CanonicalFunction(string name)
        {
            string lower = NecronomiDom.Dom.Names.AsciiLower(name);
            return lower switch
            {
                "translatex" => "translateX", "translatey" => "translateY", "translatez" => "translateZ",
                "scalex" => "scaleX", "scaley" => "scaleY", "scalez" => "scaleZ",
                "rotatex" => "rotateX", "rotatey" => "rotateY", "rotatez" => "rotateZ",
                "skewx" => "skewX", "skewy" => "skewY", _ => lower,
            };
        }
    }
}
