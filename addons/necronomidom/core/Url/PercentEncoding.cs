using System;
using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Url
{
    /// <summary>Percent-encode sets (URL Standard § 1.3) used by the URL parser.</summary>
    internal enum PercentEncodeSet { C0Control, Fragment, Query, SpecialQuery, Path, Userinfo }

    /// <summary>UTF-8 percent-encoding and percent-decoding (URL Standard § 1.3 "Percent-encoded bytes").</summary>
    internal static class PercentEncoding
    {
        public static bool InSet(int c, PercentEncodeSet set)
        {
            if (c < 0x20 || c > 0x7E) return true;
            switch (set)
            {
                case PercentEncodeSet.C0Control:
                    return false;
                case PercentEncodeSet.Fragment:
                    return c is ' ' or '"' or '<' or '>' or '`';
                case PercentEncodeSet.Query:
                    return c is ' ' or '"' or '#' or '<' or '>';
                case PercentEncodeSet.SpecialQuery:
                    return c is ' ' or '"' or '#' or '<' or '>' or '\'';
                case PercentEncodeSet.Path:
                    return c is ' ' or '"' or '#' or '<' or '>' or '?' or '^' or '`' or '{' or '}';
                default:
                    return c is ' ' or '"' or '#' or '<' or '>' or '?' or '^' or '`' or '{' or '}'
                        or '/' or ':' or ';' or '=' or '@' or '[' or '\\' or ']' or '|';
            }
        }

        /// <summary>UTF-8 percent-encodes one scalar value (lone surrogates become U+FFFD as in "scalar value string").</summary>
        public static void AppendEncoded(StringBuilder sb, int codePoint, PercentEncodeSet set)
        {
            if (!InSet(codePoint, set))
            {
                sb.Append((char)codePoint);
                return;
            }
            Span<byte> bytes = stackalloc byte[4];
            var rune = Rune.IsValid(codePoint) ? new Rune(codePoint) : Rune.ReplacementChar;
            var n = rune.EncodeToUtf8(bytes);
            for (var i = 0; i < n; i++) AppendByte(sb, bytes[i]);
        }

        public static string Encode(string input, PercentEncodeSet set)
        {
            var sb = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var cp = ReadCodePoint(input, ref i);
                AppendEncoded(sb, cp, set);
            }
            return sb.ToString();
        }

        /// <summary>Percent-decode (§ 1.3) to bytes.</summary>
        public static byte[] DecodeToBytes(string input)
        {
            var utf8 = Encoding.UTF8.GetBytes(input);
            var output = new List<byte>(utf8.Length);
            for (var i = 0; i < utf8.Length; i++)
            {
                var b = utf8[i];
                if (b == '%' && i + 2 < utf8.Length && IsHex(utf8[i + 1]) && IsHex(utf8[i + 2]))
                {
                    output.Add((byte)((HexValue(utf8[i + 1]) << 4) | HexValue(utf8[i + 2])));
                    i += 2;
                }
                else
                {
                    output.Add(b);
                }
            }
            return output.ToArray();
        }

        /// <summary>Percent-decode then UTF-8 decode without BOM (replacement on invalid sequences).</summary>
        public static string DecodeToString(string input) => new UTF8Encoding(false, false).GetString(DecodeToBytes(input));

        /// <summary>Reads the scalar value at <paramref name="i"/>, advancing past a surrogate pair; lone surrogates map to U+FFFD.</summary>
        public static int ReadCodePoint(string s, ref int i)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                var cp = char.ConvertToUtf32(c, s[i + 1]);
                i++;
                return cp;
            }
            return char.IsSurrogate(c) ? 0xFFFD : c;
        }

        public static bool IsHex(int c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        private static int HexValue(int c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

        private static void AppendByte(StringBuilder sb, byte b)
        {
            const string hex = "0123456789ABCDEF";
            sb.Append('%').Append(hex[b >> 4]).Append(hex[b & 0xF]);
        }
    }
}
