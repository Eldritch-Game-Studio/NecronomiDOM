using System;
using System.Globalization;

namespace NecronomiDom.Css.Syntax
{
    /// <summary>CSS Syntax 3 § 4.3.3–4.3.15 sub-algorithms of the tokenizer.</summary>
    public sealed partial class CssTokenizer
    {
        /// <summary>§ 4.3.3 consume a numeric token (reconsumes from <paramref name="start"/>).</summary>
        private CssToken ConsumeNumeric(int start)
        {
            _pos = start;
            var sign = '\0';
            var type = CssNumberType.Integer;
            if (Peek(0) == '+' || Peek(0) == '-') sign = _s[_pos++];
            while (IsDigit(Peek(0))) _pos++;
            if (Peek(0) == '.' && IsDigit(Peek(1)))
            {
                _pos++;
                while (IsDigit(Peek(0))) _pos++;
                type = CssNumberType.Number;
            }
            var e = Peek(0);
            if (e == 'e' || e == 'E')
            {
                var s = Peek(1);
                var digitAt = s == '+' || s == '-' ? 2 : 1;
                if (IsDigit(Peek(digitAt)))
                {
                    _pos += digitAt;
                    while (IsDigit(Peek(0))) _pos++;
                    type = CssNumberType.Number;
                }
            }
            var repr = _s.Substring(start, _pos - start);
            var value = double.Parse(repr, NumberStyles.Float, CultureInfo.InvariantCulture);
            CssToken token;
            if (WouldStartIdent(Peek(0), Peek(1), Peek(2)))
            {
                var unit = ConsumeIdentSequence();
                token = Make(CssTokenKind.Dimension, start) with { Unit = unit };
            }
            else if (Peek(0) == '%')
            {
                _pos++;
                token = Make(CssTokenKind.Percentage, start);
            }
            else token = Make(CssTokenKind.Number, start);
            return token with { Number = value, NumberType = type, Representation = repr, SignCharacter = sign };
        }

        /// <summary>§ 4.3.4 consume an ident-like token (ident, function, url or bad-url).</summary>
        private CssToken ConsumeIdentLike(int start)
        {
            _pos = start;
            var name = ConsumeIdentSequence();
            if (Peek(0) != '(') return Make(CssTokenKind.Ident, start, name);
            _pos++;
            if (!Dom.Names.AsciiEqual(name, "url")) return Make(CssTokenKind.Function, start, name);
            while (IsWhitespace(Peek(0)) && IsWhitespace(Peek(1))) _pos++;
            var a = Peek(0);
            if (a == '"' || a == '\'' || (IsWhitespace(a) && (Peek(1) == '"' || Peek(1) == '\'')))
                return Make(CssTokenKind.Function, start, name);
            return ConsumeUrl(start);
        }

        /// <summary>§ 4.3.5 consume a string token ending at <paramref name="ending"/>.</summary>
        private CssToken ConsumeString(char ending, int start)
        {
            _sb.Clear();
            while (true)
            {
                if (_pos >= _len)
                {
                    Error(start, "css-unterminated-string", "String is not closed before the end of the input.");
                    return Make(CssTokenKind.String, start, _sb.ToString());
                }
                var c = _s[_pos++];
                if (c == ending) return Make(CssTokenKind.String, start, _sb.ToString()) with { Representation = _s.Substring(start, _pos - start) };
                if (c == '\n')
                {
                    _pos--;
                    Error(start, "css-bad-string", "Unescaped newline inside a string.");
                    return Make(CssTokenKind.BadString, start);
                }
                if (c != '\\')
                {
                    _sb.Append(c);
                    continue;
                }
                if (_pos >= _len) continue;
                if (Peek(0) == '\n') _pos++;
                else AppendEscaped();
            }
        }

        /// <summary>§ 4.3.6 consume a url token ("url(" already consumed).</summary>
        private CssToken ConsumeUrl(int start)
        {
            _sb.Clear();
            while (IsWhitespace(Peek(0))) _pos++;
            while (true)
            {
                if (_pos >= _len)
                {
                    Error(start, "css-unterminated-url", "url( is not closed before the end of the input.");
                    return Make(CssTokenKind.Url, start, _sb.ToString());
                }
                var c = _s[_pos++];
                if (c == ')') return Make(CssTokenKind.Url, start, _sb.ToString());
                if (IsWhitespace(c))
                {
                    while (IsWhitespace(Peek(0))) _pos++;
                    if (_pos >= _len)
                    {
                        Error(start, "css-unterminated-url", "url( is not closed before the end of the input.");
                        return Make(CssTokenKind.Url, start, _sb.ToString());
                    }
                    if (Peek(0) == ')')
                    {
                        _pos++;
                        return Make(CssTokenKind.Url, start, _sb.ToString());
                    }
                    return BadUrl(start, "Whitespace inside an unquoted url( value.");
                }
                if (c == '"' || c == '\'' || c == '(' || IsNonPrintable(c))
                    return BadUrl(start, "Invalid code point inside an unquoted url( value.");
                if (c == '\\')
                {
                    if (!IsValidEscape(c, Peek(0))) return BadUrl(start, "Invalid escape inside an unquoted url( value.");
                    AppendEscaped();
                    continue;
                }
                _sb.Append(c);
            }
        }

        /// <summary>§ 4.3.15 consume the remnants of a bad url, then emit the bad-url token.</summary>
        private CssToken BadUrl(int start, string message)
        {
            Error(start, "css-bad-url", message);
            while (_pos < _len)
            {
                var c = _s[_pos++];
                if (c == ')') break;
                if (IsValidEscape(c, Peek(0)))
                {
                    var saved = _sb.Length;
                    AppendEscaped();
                    _sb.Length = saved;
                }
            }
            return Make(CssTokenKind.BadUrl, start);
        }

        /// <summary>§ 4.3.12 consume an ident sequence (substring fast path when there are no escapes).</summary>
        private string ConsumeIdentSequence()
        {
            var start = _pos;
            while (IsIdentChar(Peek(0))) _pos++;
            if (!IsValidEscape(Peek(0), Peek(1))) return _s.Substring(start, _pos - start);
            _sb.Clear();
            _sb.Append(_s, start, _pos - start);
            while (true)
            {
                var c = Peek(0);
                if (IsIdentChar(c))
                {
                    _sb.Append(c);
                    _pos++;
                }
                else if (IsValidEscape(c, Peek(1)))
                {
                    _pos++;
                    AppendEscaped();
                }
                else return _sb.ToString();
            }
        }

        /// <summary>§ 4.3.7 consume an escaped code point (the backslash is already consumed).</summary>
        private void AppendEscaped()
        {
            if (_pos >= _len)
            {
                Error(_pos, "css-eof-in-escape", "Escape at the end of the input.");
                _sb.Append('\uFFFD');
                return;
            }
            var c = _s[_pos++];
            if (!IsHexDigit(c))
            {
                _sb.Append(c);
                if (char.IsHighSurrogate(c) && _pos < _len) _sb.Append(_s[_pos++]);
                return;
            }
            var value = HexValue(c);
            for (var n = 1; n < 6 && IsHexDigit(Peek(0)); n++) value = value * 16 + HexValue(_s[_pos++]);
            if (IsWhitespace(Peek(0))) _pos++;
            if (value == 0 || (value >= 0xD800 && value <= 0xDFFF) || value > 0x10FFFF) _sb.Append('\uFFFD');
            else if (value < 0x10000) _sb.Append((char)value);
            else _sb.Append(char.ConvertFromUtf32(value));
        }

        private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

        internal static bool IsDigit(char c) => c >= '0' && c <= '9';

        internal static bool IsHexDigit(char c) => IsDigit(c) || ((c | 0x20) >= 'a' && (c | 0x20) <= 'f');

        internal static bool IsWhitespace(char c) => c == ' ' || c == '\n' || c == '\t';

        internal static bool IsNonPrintable(char c) => c <= '\u0008' || c == '\u000B' || (c >= '\u000E' && c <= '\u001F') || c == '\u007F';

        /// <summary>§ 4.2 non-ASCII ident code point; surrogates stand for code points ≥ U+10000 (lone ones were filtered).</summary>
        internal static bool IsNonAsciiIdent(char c) =>
            c == '\u00B7' || (c >= '\u00C0' && c <= '\u00D6') || (c >= '\u00D8' && c <= '\u00F6') || (c >= '\u00F8' && c <= '\u037D') ||
            (c >= '\u037F' && c <= '\u1FFF') || c == '\u200C' || c == '\u200D' || c == '\u203F' || c == '\u2040' ||
            (c >= '\u2070' && c <= '\u218F') || (c >= '\u2C00' && c <= '\u2FEF') || (c >= '\u3001' && c <= '\uD7FF') ||
            (c >= '\uF900' && c <= '\uFDCF') || (c >= '\uFDF0' && c <= '\uFFFD') || char.IsSurrogate(c);

        internal static bool IsIdentStart(char c) => (c | 0x20) >= 'a' && (c | 0x20) <= 'z' || c == '_' || (c >= 0x80 && IsNonAsciiIdent(c));

        internal static bool IsIdentChar(char c) => IsIdentStart(c) || IsDigit(c) || c == '-';

        /// <summary>§ 4.3.8 check if two code points are a valid escape ('\0' is the EOF sentinel; an escaped EOF is valid).</summary>
        internal static bool IsValidEscape(char first, char second) => first == '\\' && second != '\n';

        /// <summary>§ 4.3.9 check if three code points would start an ident sequence.</summary>
        internal static bool WouldStartIdent(char a, char b, char c)
        {
            if (a == '-') return IsIdentStart(b) || b == '-' || IsValidEscape(b, c);
            if (IsIdentStart(a)) return true;
            return a == '\\' && IsValidEscape(a, b);
        }

        /// <summary>§ 4.3.10 check if three code points would start a number.</summary>
        internal static bool WouldStartNumber(char a, char b, char c)
        {
            if (a == '+' || a == '-') return IsDigit(b) || (b == '.' && IsDigit(c));
            if (a == '.') return IsDigit(b);
            return IsDigit(a);
        }
    }
}
