using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Url
{
    /// <summary>
    /// The basic URL parser of URL Standard § 4.4 (no state override, UTF-8 only). Additionally reports
    /// whether a double-dot segment tried to shorten an already-empty path (climbing above the root),
    /// which <see cref="ResourceUrlPolicy"/> rejects.
    /// </summary>
    internal sealed partial class WebUrlParser
    {
        private enum State
        {
            SchemeStart, Scheme, NoScheme, SpecialRelativeOrAuthority, PathOrAuthority, Relative, RelativeSlash,
            SpecialAuthoritySlashes, SpecialAuthorityIgnoreSlashes, Authority, Host, Port, File, FileSlash, FileHost,
            PathStart, Path, OpaquePath, Query, Fragment,
        }

        private const int Eof = -1;

        private readonly int[] _input;
        private readonly WebUrl? _base;
        private string _scheme = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string? _host;
        private int? _port;
        private List<string> _path = new();
        private StringBuilder? _opaquePath;
        private string? _query;
        private StringBuilder? _fragment;
        private bool _climbed;

        private WebUrlParser(int[] input, WebUrl? baseUrl)
        {
            _input = input;
            _base = baseUrl;
        }

        private bool Special => WebUrl.IsSpecialScheme(_scheme);

        public static WebUrl? Parse(string input, WebUrl? baseUrl, out bool climbedAboveRoot)
        {
            var parser = new WebUrlParser(Preprocess(input), baseUrl);
            var url = parser.Run();
            climbedAboveRoot = parser._climbed;
            return url;
        }

        /// <summary>Strips leading/trailing C0 control or space and removes ASCII tab/newline (§ 4.4 steps 1–3).</summary>
        private static int[] Preprocess(string input)
        {
            var start = 0;
            var end = input.Length;
            while (start < end && input[start] <= 0x20) start++;
            while (end > start && input[end - 1] <= 0x20) end--;
            var cps = new List<int>(end - start);
            for (var i = start; i < end; i++)
            {
                var c = input[i];
                if (c is '\t' or '\n' or '\r') continue;
                cps.Add(PercentEncoding.ReadCodePoint(input, ref i));
            }
            return cps.ToArray();
        }

        private int At(int pointer) => pointer >= 0 && pointer < _input.Length ? _input[pointer] : Eof;

        private bool RemainingStartsWith(int pointer, char c) => At(pointer + 1) == c;

        private bool RemainingStartsWithTwoHex(int pointer) =>
            PercentEncoding.IsHex(At(pointer + 1)) && PercentEncoding.IsHex(At(pointer + 2));

        private WebUrl Build() => new(_scheme, _username, _password, _host, _port,
            _opaquePath == null ? _path : null, _opaquePath?.ToString(), _query, _fragment?.ToString());

        private void CopyAuthorityFromBase()
        {
            _username = _base!.Username;
            _password = _base.Password;
            _host = _base.Host;
            _port = _base.Port;
        }

        /// <summary>"shorten a url's path" (§ 4.1).</summary>
        private void ShortenPath()
        {
            if (_scheme == "file" && _path.Count == 1 && IsNormalizedWindowsDriveLetter(_path[0])) return;
            if (_path.Count > 0) _path.RemoveAt(_path.Count - 1);
        }

        private static bool IsAsciiAlpha(int c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

        private static bool IsWindowsDriveLetter(string s) =>
            s.Length == 2 && IsAsciiAlpha(s[0]) && s[1] is ':' or '|';

        private static bool IsNormalizedWindowsDriveLetter(string s) => s.Length == 2 && IsAsciiAlpha(s[0]) && s[1] == ':';

        private bool StartsWithWindowsDriveLetter(int pointer)
        {
            if (!IsAsciiAlpha(At(pointer)) || At(pointer + 1) is not (':' or '|')) return false;
            var third = At(pointer + 2);
            return third is Eof or '/' or '\\' or '?' or '#';
        }

        private static bool IsSingleDot(string s) => s == "." || s.Equals("%2e", System.StringComparison.OrdinalIgnoreCase);

        private static bool IsDoubleDot(string s) => s switch
        {
            ".." => true,
            _ => s.Length is 4 or 6 && s.Replace("%2e", ".", System.StringComparison.OrdinalIgnoreCase) == "..",
        };
    }
}
