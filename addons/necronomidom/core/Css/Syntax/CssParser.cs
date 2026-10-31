using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.Syntax
{
    /// <summary>CSS Syntax 3 § 5 parsing entry points; block parsing follows the nesting-aware editor's draft.</summary>
    public static partial class CssParser
    {
        public static IReadOnlyList<ComponentValue> ParseComponentValueList(string css, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "") => ParseComponentValueList(css, diagnostics, sourcePath, out _);
        internal static IReadOnlyList<ComponentValue> ParseComponentValueList(string css, IList<WebDiagnostic>? diagnostics, string sourcePath, out string source)
        {
            var tokens = CssTokenizer.Tokenize(css, diagnostics, sourcePath, out source);
            var reader = new Reader(tokens, diagnostics, sourcePath, source.Length);
            return reader.Values(null);
        }
        public static ComponentValue? ParseComponentValue(string css, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "")
        {
            var values = Trim(ParseComponentValueList(css, diagnostics, sourcePath));
            return values.Count == 1 ? values[0] : null;
        }
        public static IReadOnlyList<IReadOnlyList<ComponentValue>> ParseCommaSeparatedComponentValues(string css) => SplitComma(ParseComponentValueList(css));
        public static IReadOnlyList<IReadOnlyList<ComponentValue>> SplitComma(IReadOnlyList<ComponentValue> values)
        {
            var result = new List<IReadOnlyList<ComponentValue>>();
            var start = 0;
            for (var i = 0; i <= values.Count; i++)
            {
                if (i < values.Count && !Is(values[i], CssTokenKind.Comma)) continue;
                result.Add(Slice(values, start, i - start));
                start = i + 1;
            }
            return result;
        }
        public static IReadOnlyList<RawRule> ParseStylesheet(string css, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "") => ParseStylesheet(css, diagnostics, sourcePath, out _);
        internal static IReadOnlyList<RawRule> ParseStylesheet(string css, IList<WebDiagnostic>? diagnostics, string sourcePath, out string source) =>
            Rules(ParseComponentValueList(css, diagnostics, sourcePath, out source), true, diagnostics, sourcePath);
        public static IReadOnlyList<RawRule> ParseRuleList(string css, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "") =>
            Rules(ParseComponentValueList(css, diagnostics, sourcePath), false, diagnostics, sourcePath);
        public static IReadOnlyList<RawRule> ParseRuleList(IReadOnlyList<ComponentValue> values) => Rules(values, false, null, "");
        public static RawRule? ParseRule(string css, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "") => ParseRule(css, diagnostics, sourcePath, out _);
        internal static RawRule? ParseRule(string css, IList<WebDiagnostic>? diagnostics, string sourcePath, out string source)
        {
            var values = Trim(ParseComponentValueList(css, diagnostics, sourcePath, out source));
            var position = 0;
            var rule = ConsumeRule(values, ref position, diagnostics, sourcePath);
            while (position < values.Count && Is(values[position], CssTokenKind.Whitespace)) position++;
            return position == values.Count ? rule : null;
        }
        public static RawDeclaration? ParseDeclaration(string css, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "") =>
            Declaration(Trim(ParseComponentValueList(css, diagnostics, sourcePath)), diagnostics, sourcePath);
        public static CssBlockContents ParseBlockContents(string css, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "") =>
            ParseBlockContents(ParseComponentValueList(css, diagnostics, sourcePath), diagnostics, sourcePath);
        public static CssBlockContents ParseBlockContents(IReadOnlyList<ComponentValue> values, IList<WebDiagnostic>? diagnostics = null, string sourcePath = "")
        {
            var items = new List<object>();
            var p = 0;
            while (p < values.Count)
            {
                if (Is(values[p], CssTokenKind.Whitespace) || Is(values[p], CssTokenKind.Semicolon)) { p++; continue; }
                if (Is(values[p], CssTokenKind.AtKeyword))
                {
                    var rule = ConsumeRule(values, ref p, diagnostics, sourcePath);
                    if (rule != null) items.Add(rule);
                    continue;
                }
                var end = p;
                while (end < values.Count && !Is(values[end], CssTokenKind.Semicolon)) end++;
                var declaration = Declaration(Trim(Slice(values, p, end - p)), null, sourcePath);
                if (declaration != null)
                {
                    items.Add(declaration);
                    p = end < values.Count ? end + 1 : end;
                    continue;
                }
                var nested = ConsumeRule(values, ref p, diagnostics, sourcePath, true);
                if (nested != null) items.Add(nested);
            }
            return new CssBlockContents(items);
        }
        public static bool Is(ComponentValue value, CssTokenKind kind) => value is PreservedToken token && token.Token.Kind == kind;
        public static bool IsIdent(ComponentValue value, string ident) => value is PreservedToken token && token.Token.IsIdent(ident);
        public static bool IsDelim(ComponentValue value, char c) => value is PreservedToken token && token.Token.IsDelim(c);
        public static IReadOnlyList<ComponentValue> Trim(IReadOnlyList<ComponentValue> values)
        {
            var start = 0;
            var end = values.Count;
            while (start < end && Is(values[start], CssTokenKind.Whitespace)) start++;
            while (end > start && Is(values[end - 1], CssTokenKind.Whitespace)) end--;
            return start == 0 && end == values.Count ? values : Slice(values, start, end - start);
        }
        public static IReadOnlyList<ComponentValue> Slice(IReadOnlyList<ComponentValue> values, int start, int length)
        {
            var result = new ComponentValue[length];
            for (var i = 0; i < length; i++) result[i] = values[start + i];
            return result;
        }
        private sealed class Reader
        {
            private readonly IReadOnlyList<CssToken> _tokens;
            private readonly IList<WebDiagnostic>? _diagnostics;
            private readonly string _file;
            private int _p;
            private readonly int _length;
            public Reader(IReadOnlyList<CssToken> tokens, IList<WebDiagnostic>? diagnostics, string file, int length)
            { _tokens = tokens; _diagnostics = diagnostics; _file = file; _length = length; }
            public IReadOnlyList<ComponentValue> Values(CssTokenKind? closing)
            {
                var values = new List<ComponentValue>();
                while (_p < _tokens.Count)
                {
                    var token = _tokens[_p++];
                    if (token.Kind == closing) return values;
                    if (token.Kind == CssTokenKind.Function)
                    {
                        var arguments = Values(CssTokenKind.RightParen);
                        values.Add(new FunctionValue(token, arguments) { EndOffset = End(CssTokenKind.RightParen) });
                    }
                    else if (token.Kind == CssTokenKind.LeftBrace || token.Kind == CssTokenKind.LeftBracket || token.Kind == CssTokenKind.LeftParen)
                    {
                        var close = token.Kind == CssTokenKind.LeftBrace ? CssTokenKind.RightBrace : token.Kind == CssTokenKind.LeftBracket ? CssTokenKind.RightBracket : CssTokenKind.RightParen;
                        var contents = Values(close);
                        values.Add(new SimpleBlock(token, contents) { EndOffset = End(close) });
                    }
                    else values.Add(new PreservedToken(token) { EndOffset = _p < _tokens.Count ? _tokens[_p].Offset : _length });
                }
                if (closing.HasValue)
                {
                    var token = _tokens.Count == 0 ? default : _tokens[_tokens.Count - 1];
                    _diagnostics?.Add(new WebDiagnostic(WebDiagnosticSeverity.Warning, _file, token.Line, token.Column, "css-unclosed-block", "Block or function ended at EOF."));
                }
                return values;
            }
            private int End(CssTokenKind closing) => _p > 0 && _tokens[_p - 1].Kind == closing ? _tokens[_p - 1].Offset + 1 : _length;
        }
    }
}
