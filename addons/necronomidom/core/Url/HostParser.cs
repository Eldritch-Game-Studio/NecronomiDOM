using System.Text;

namespace NecronomiDom.Url
{
    /// <summary>
    /// The host parser (URL Standard § 3.5). Opaque hosts (non-special schemes) are implemented exactly. Domains of
    /// special schemes are ASCII-lowercased and checked for forbidden domain code points; IDNA (UTS #46) mapping,
    /// IPv4 number canonicalisation and IPv6 compression are not implemented — NecronomiDOM only parses special-scheme URLs
    /// to recognise and reject them (markup profile resource policy), so a non-ASCII domain is treated as failure.
    /// </summary>
    internal static class HostParser
    {
        public static string? Parse(string input, bool isOpaque)
        {
            if (input.Length > 0 && input[0] == '[')
            {
                if (input[^1] != ']') return null;
                for (var i = 1; i < input.Length - 1; i++)
                    if (!(PercentEncoding.IsHex(input[i]) || input[i] is ':' or '.')) return null;
                return input.ToLowerInvariant();
            }
            if (isOpaque) return ParseOpaqueHost(input);
            var domain = PercentEncoding.DecodeToString(input);
            var sb = new StringBuilder(domain.Length);
            foreach (var c in domain)
            {
                if (c > 0x7E || IsForbiddenDomainCodePoint(c)) return null;
                sb.Append(c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>"opaque-host parser" (§ 3.5): forbidden host code points fail; the rest is C0-control percent-encoded.</summary>
        private static string? ParseOpaqueHost(string input)
        {
            foreach (var c in input)
                if (IsForbiddenHostCodePoint(c)) return null;
            return PercentEncoding.Encode(input, PercentEncodeSet.C0Control);
        }

        private static bool IsForbiddenHostCodePoint(char c) =>
            c is '\0' or '\t' or '\n' or '\r' or ' ' or '#' or '/' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']'
                or '^' or '|';

        private static bool IsForbiddenDomainCodePoint(char c) =>
            IsForbiddenHostCodePoint(c) || c <= 0x1F || c is '%' or (char)0x7F;
    }
}
