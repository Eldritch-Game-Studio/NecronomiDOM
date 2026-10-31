using System;
using System.Collections.Generic;
using System.Globalization;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorParser
    {
        public static NthExpression? ParseNth(IReadOnlyList<ComponentValue> values, bool allowOf = true, SelectorParseOptions? options = null)
        {
            var end = values.Count;
            SelectorList? of = null;
            for (var i = 0; i < values.Count; i++)
            {
                if (!CssParser.IsIdent(values[i], "of")) continue;
                if (!allowOf || i == 0 || i + 1 >= values.Count) return null;
                end = i;
                of = ParseSelectorList(CssParser.Slice(values, i + 1, values.Count - i - 1), (options ?? new SelectorParseOptions()) with { Relative = false, Forgiving = false, AllowPseudoElements = false, IgnoreSubjectDefaultNamespace = false, CompoundOnly = false, PartArgument = false });
                if (of == null) return null;
                break;
            }
            var expression = CssParser.Trim(CssParser.Slice(values, 0, end));
            if (expression.Count == 0) return null;
            if (expression.Count == 1 && CssParser.IsIdent(expression[0], "odd")) return new NthExpression(2, 1, of);
            if (expression.Count == 1 && CssParser.IsIdent(expression[0], "even")) return new NthExpression(2, 0, of);
            var p = 0;
            var plus = CssParser.IsDelim(expression[p], '+');
            if (plus) { p++; if (p >= expression.Count || CssParser.Is(expression[p], CssTokenKind.Whitespace)) return null; }
            if (expression[p] is not PreservedToken first) return null;
            var token = first.Token;
            p++;
            if (token.Kind == CssTokenKind.Number && !plus)
                return p == expression.Count && Integer(token, out var integer) ? new NthExpression(0, integer, of) : null;
            var a = 1;
            string unit;
            if (token.Kind == CssTokenKind.Dimension && !plus)
            { if (!Integer(token, out a)) return null; unit = Dom.Names.AsciiLower(token.Unit); }
            else if (token.Kind == CssTokenKind.Ident)
            {
                unit = Dom.Names.AsciiLower(token.Value);
                if (unit.StartsWith("-", StringComparison.Ordinal)) { if (plus) return null; a = -1; unit = unit.Substring(1); }
            }
            else return null;
            if (unit != "n" && !unit.StartsWith("n-", StringComparison.Ordinal)) return null;
            var embeddedMinus = unit.StartsWith("n-", StringComparison.Ordinal);
            var b = 0;
            if (unit.Length > 2)
            {
                if (!Digits(unit.AsSpan(2)) || !int.TryParse("-" + unit.Substring(2), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out b)) return null;
                SkipNthSpace(expression, ref p);
                return p == expression.Count ? new NthExpression(a, b, of) : null;
            }
            SkipNthSpace(expression, ref p);
            if (p == expression.Count) return embeddedMinus ? null : new NthExpression(a, 0, of);
            var sign = embeddedMinus ? -1 : 1;
            var separateSign = false;
            if (!embeddedMinus && (CssParser.IsDelim(expression[p], '+') || CssParser.IsDelim(expression[p], '-')))
            {
                sign = CssParser.IsDelim(expression[p], '-') ? -1 : 1;
                separateSign = true;
                p++;
                SkipNthSpace(expression, ref p);
            }
            if (p >= expression.Count || expression[p++] is not PreservedToken number || number.Token.Kind != CssTokenKind.Number || !Integer(number.Token, out b)) return null;
            if (embeddedMinus || separateSign)
            {
                if (number.Token.SignCharacter != '\0' || b < 0 || (sign < 0 && b == int.MinValue)) return null;
                b *= sign;
            }
            else if (number.Token.SignCharacter == '\0') return null;
            SkipNthSpace(expression, ref p);
            return p == expression.Count ? new NthExpression(a, b, of) : null;
        }
        private static bool Integer(CssToken token, out int number)
        {
            number = 0;
            if (token.NumberType != CssNumberType.Integer || token.Number < int.MinValue || token.Number > int.MaxValue) return false;
            number = (int)token.Number;
            return true;
        }
        private static bool Digits(ReadOnlySpan<char> text)
        { if (text.Length == 0) return false; foreach (var c in text) if (c < '0' || c > '9') return false; return true; }
        private static void SkipNthSpace(IReadOnlyList<ComponentValue> values, ref int p)
        { while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++; }
    }
}
