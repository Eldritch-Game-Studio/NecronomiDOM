using System;
using System.Globalization;
using System.Text;

namespace NecronomiDom.Css.Syntax
{
    /// <summary>
    /// CSSOM 1 § 2.1 common serializing idioms: serialize an identifier, a string and a URL; plus number formatting as
    /// Chrome does it (6 significant digits, ECMAScript <c>toPrecision</c> style exponent form, trailing zeros trimmed,
    /// "-0" → "0"), the tie-breaker CSSOM leaves open for "serialize a number".
    /// </summary>
    public static class CssSerialization
    {
        public static string SerializeIdentifier(string ident)
        {
            if (!NeedsIdentEscaping(ident)) return ident;
            var sb = new StringBuilder(ident.Length + 8);
            AppendIdentifier(sb, ident);
            return sb.ToString();
        }

        /// <summary>Serialize an identifier (CSSOM § 2.1).</summary>
        public static void AppendIdentifier(StringBuilder sb, string ident)
        {
            for (var i = 0; i < ident.Length; i++)
            {
                var c = ident[i];
                if (c == '\0') sb.Append('\uFFFD');
                else if ((c >= '\u0001' && c <= '\u001F') || c == '\u007F') AppendCodePointEscape(sb, c);
                else if (i == 0 && CssTokenizer.IsDigit(c)) AppendCodePointEscape(sb, c);
                else if (i == 1 && CssTokenizer.IsDigit(c) && ident[0] == '-') AppendCodePointEscape(sb, c);
                else if (i == 0 && c == '-' && ident.Length == 1) sb.Append('\\').Append(c);
                else if (IsPlainIdentChar(c)) sb.Append(c);
                else sb.Append('\\').Append(c);
            }
        }

        /// <summary>Serialize a string (CSSOM § 2.1), always double-quoted.</summary>
        public static string SerializeString(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            AppendString(sb, value);
            return sb.ToString();
        }

        public static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                if (c == '\0') sb.Append('\uFFFD');
                else if ((c >= '\u0001' && c <= '\u001F') || c == '\u007F') AppendCodePointEscape(sb, c);
                else if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else sb.Append(c);
            }
            sb.Append('"');
        }

        /// <summary>Serialize a URL (CSSOM § 2.1): <c>url("…")</c>.</summary>
        public static string SerializeUrl(string url)
        {
            var sb = new StringBuilder(url.Length + 7);
            sb.Append("url(");
            AppendString(sb, url);
            return sb.Append(')').ToString();
        }

        /// <summary>
        /// Chrome's number formatting (WTF NumberToFixedPrecisionString with 6 significant digits): fixed notation when
        /// the decimal exponent is in [-6, 6), else <c>d.ddddde±x</c>; trailing zeros removed.
        /// </summary>
        public static string SerializeNumber(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "infinity";
            if (double.IsNegativeInfinity(value)) return "-infinity";
            if (value == 0) return "0";
            if (Math.Abs(value) < 1e15 && value == Math.Floor(value) && Math.Abs(value) < 1e6)
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            var sci = value.ToString("E5", CultureInfo.InvariantCulture);
            var ePos = sci.IndexOf('E');
            var exponent = int.Parse(sci.AsSpan(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (exponent < -6 || exponent >= 6)
            {
                var mantissa = TrimZeros(sci.Substring(0, ePos));
                return mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
            }
            var fixedText = value.ToString("F" + (5 - exponent).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var trimmed = TrimZeros(fixedText);
            return trimmed == "-0" ? "0" : trimmed;
        }

        private static string TrimZeros(string text)
        {
            if (text.IndexOf('.') < 0) return text;
            var end = text.Length;
            while (text[end - 1] == '0') end--;
            if (text[end - 1] == '.') end--;
            return text.Substring(0, end);
        }

        private static void AppendCodePointEscape(StringBuilder sb, char c) =>
            sb.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');

        private static bool IsPlainIdentChar(char c) =>
            c == '-' || c == '_' || CssTokenizer.IsDigit(c) || ((c | 0x20) >= 'a' && (c | 0x20) <= 'z') || (c >= 0x80 && CssTokenizer.IsNonAsciiIdent(c));

        private static bool NeedsIdentEscaping(string ident)
        {
            if (ident.Length == 0) return false;
            if (CssTokenizer.IsDigit(ident[0]) || (ident[0] == '-' && (ident.Length == 1 || CssTokenizer.IsDigit(ident[1])))) return true;
            foreach (var c in ident)
            {
                if (!IsPlainIdentChar(c)) return true;
            }
            return false;
        }
    }
}
