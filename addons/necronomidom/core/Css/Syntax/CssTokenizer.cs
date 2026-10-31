using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Css.Syntax
{
    /// <summary>
    /// CSS Syntax 3 tokenizer: § 3.3 input preprocessing (CR/CRLF/FF → LF, NUL and lone surrogates → U+FFFD) and
    /// § 4.3 "consume a token" with every sub-algorithm (comments, numeric, ident-like, string, url, escapes).
    /// The returned list never contains the EOF token. Parse errors become <see cref="WebDiagnostic"/> warnings with
    /// the 1-based line/column of the offending code point. Unicode-range tokens (§ 4.3.14) are not produced: the
    /// shared <see cref="CssTokenKind"/> set has none, so unicode-range descriptors are parsed from ordinary tokens.
    /// </summary>
    public sealed partial class CssTokenizer
    {
        private static readonly string[] AsciiStrings = BuildAsciiStrings();
        private readonly string _s;
        private readonly int _len;
        private readonly IList<WebDiagnostic>? _diagnostics;
        private readonly string _file;
        private readonly StringBuilder _sb = new StringBuilder();
        private int _pos;
        private int _line = 1;
        private int _lineStart;
        private int _scanned;

        private CssTokenizer(string preprocessed, IList<WebDiagnostic>? diagnostics, string file)
        {
            _s = preprocessed;
            _len = preprocessed.Length;
            _diagnostics = diagnostics;
            _file = file;
        }

        /// <summary>Tokenizes <paramref name="css"/> (Syntax 3 § 3.3 + § 4) without diagnostics.</summary>
        public static IReadOnlyList<CssToken> Tokenize(string css) => Tokenize(css, null, string.Empty);

        /// <summary>Tokenizes <paramref name="css"/>, reporting tokenizer parse errors to <paramref name="diagnostics"/>.</summary>
        public static IReadOnlyList<CssToken> Tokenize(string css, IList<WebDiagnostic>? diagnostics, string sourcePath) => Tokenize(css, diagnostics, sourcePath, out _);

        internal static IReadOnlyList<CssToken> Tokenize(string css, IList<WebDiagnostic>? diagnostics, string sourcePath, out string preprocessed)
        {
            preprocessed = Preprocess(css);
            var tokenizer = new CssTokenizer(preprocessed, diagnostics, sourcePath);
            var tokens = new List<CssToken>(css.Length / 3 + 4);
            while (true)
            {
                var token = tokenizer.ConsumeToken();
                if (token.Kind == CssTokenKind.Eof) return tokens;
                tokens.Add(token);
            }
        }

        /// <summary>Syntax 3 § 3.3 "filter code points" (the normalisation half of preprocessing).</summary>
        public static string Preprocess(string input)
        {
            var clean = true;
            for (var i = 0; i < input.Length && clean; i++)
            {
                var c = input[i];
                if (c == '\r' || c == '\f' || c == '\0') clean = false;
                else if (char.IsHighSurrogate(c)) clean = i + 1 < input.Length && char.IsLowSurrogate(input[++i]);
                else if (char.IsLowSurrogate(c)) clean = false;
            }
            if (clean) return input;
            var sb = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c == '\r')
                {
                    sb.Append('\n');
                    if (i + 1 < input.Length && input[i + 1] == '\n') i++;
                }
                else if (c == '\f') sb.Append('\n');
                else if (c == '\0') sb.Append('\uFFFD');
                else if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    sb.Append(c).Append(input[++i]);
                }
                else if (char.IsSurrogate(c)) sb.Append('\uFFFD');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private char Peek(int ahead)
        {
            var i = _pos + ahead;
            return i < _len ? _s[i] : '\0';
        }

        private CssToken ConsumeToken()
        {
            ConsumeComments();
            var start = _pos;
            if (_pos >= _len) return Make(CssTokenKind.Eof, start);
            var c = _s[_pos++];
            switch (c)
            {
                case '\n':
                case '\t':
                case ' ':
                    while (IsWhitespace(Peek(0))) _pos++;
                    return Make(CssTokenKind.Whitespace, start);
                case '"':
                case '\'':
                    return ConsumeString(c, start);
                case '#':
                    if (IsIdentChar(Peek(0)) || IsValidEscape(Peek(0), Peek(1)))
                    {
                        var isId = WouldStartIdent(Peek(0), Peek(1), Peek(2));
                        return Make(CssTokenKind.Hash, start, ConsumeIdentSequence()) with { IsIdHash = isId };
                    }
                    return Delim(c, start);
                case '(': return Make(CssTokenKind.LeftParen, start);
                case ')': return Make(CssTokenKind.RightParen, start);
                case '+':
                case '.':
                    if (WouldStartNumber(c, Peek(0), Peek(1))) return ConsumeNumeric(start);
                    return Delim(c, start);
                case ',': return Make(CssTokenKind.Comma, start);
                case '-':
                    if (WouldStartNumber(c, Peek(0), Peek(1))) return ConsumeNumeric(start);
                    if (Peek(0) == '-' && Peek(1) == '>')
                    {
                        _pos += 2;
                        return Make(CssTokenKind.Cdc, start);
                    }
                    if (WouldStartIdent(c, Peek(0), Peek(1))) return ConsumeIdentLike(start);
                    return Delim(c, start);
                case ':': return Make(CssTokenKind.Colon, start);
                case ';': return Make(CssTokenKind.Semicolon, start);
                case '<':
                    if (Peek(0) == '!' && Peek(1) == '-' && Peek(2) == '-')
                    {
                        _pos += 3;
                        return Make(CssTokenKind.Cdo, start);
                    }
                    return Delim(c, start);
                case '@':
                    if (WouldStartIdent(Peek(0), Peek(1), Peek(2))) return Make(CssTokenKind.AtKeyword, start, ConsumeIdentSequence());
                    return Delim(c, start);
                case '[': return Make(CssTokenKind.LeftBracket, start);
                case '\\':
                    if (IsValidEscape(c, Peek(0))) return ConsumeIdentLike(start);
                    Error(start, "css-invalid-escape", "A backslash followed by a newline is not a valid escape.");
                    return Delim(c, start);
                case ']': return Make(CssTokenKind.RightBracket, start);
                case '{': return Make(CssTokenKind.LeftBrace, start);
                case '}': return Make(CssTokenKind.RightBrace, start);
                default:
                    if (IsDigit(c)) return ConsumeNumeric(start);
                    if (IsIdentStart(c)) return ConsumeIdentLike(start);
                    return Delim(c, start);
            }
        }

        private void ConsumeComments()
        {
            while (Peek(0) == '/' && Peek(1) == '*')
            {
                var end = _s.IndexOf("*/", _pos + 2, System.StringComparison.Ordinal);
                if (end < 0)
                {
                    Error(_pos, "css-unterminated-comment", "Comment is not closed before the end of the stylesheet.");
                    _pos = _len;
                    return;
                }
                _pos = end + 2;
            }
        }

        private CssToken Delim(char c, int start) =>
            Make(CssTokenKind.Delim, start, c < 128 ? AsciiStrings[c] : c.ToString());

        private CssToken Make(CssTokenKind kind, int start, string value = "")
        {
            Locate(start, out var line, out var column);
            return new CssToken(kind, value, Line: line, Column: column, Offset: start);
        }

        /// <summary>Advances the monotonic line cursor to <paramref name="offset"/> (token starts only grow).</summary>
        private void Locate(int offset, out int line, out int column)
        {
            if (offset < _scanned)
            {
                _scanned = 0;
                _line = 1;
                _lineStart = 0;
            }
            for (; _scanned < offset && _scanned < _len; _scanned++)
            {
                if (_s[_scanned] != '\n') continue;
                _line++;
                _lineStart = _scanned + 1;
            }
            line = _line;
            column = offset - _lineStart + 1;
        }

        private void Error(int offset, string code, string message)
        {
            if (_diagnostics == null) return;
            Locate(offset, out var line, out var column);
            _diagnostics.Add(new WebDiagnostic(WebDiagnosticSeverity.Warning, _file, line, column, code, message));
        }

        private static string[] BuildAsciiStrings()
        {
            var table = new string[128];
            for (var i = 0; i < table.Length; i++) table[i] = ((char)i).ToString();
            return table;
        }
    }
}
