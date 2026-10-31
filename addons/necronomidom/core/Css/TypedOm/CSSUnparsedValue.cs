using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    public readonly struct CSSUnparsedSegment
    {
        private CSSUnparsedSegment(string? text, CSSVariableReferenceValue? variable)
        { Text = text; Variable = variable; Tokens = text == null ? null : CssParser.ParseComponentValueList(text); }
        public string? Text { get; }
        public CSSVariableReferenceValue? Variable { get; }
        internal IReadOnlyList<ComponentValue>? Tokens { get; }
        public static implicit operator CSSUnparsedSegment(string text) => new(text ?? throw new ArgumentNullException(nameof(text)), null);
        public static implicit operator CSSUnparsedSegment(CSSVariableReferenceValue variable) => new(null, variable ?? throw new ArgumentNullException(nameof(variable)));
        public override string ToString() => Text ?? Variable!.ToString();
    }
    /// <summary>CSS Typed OM 1 § 3.4 string/variable segments, copied on construction as a WebIDL sequence.</summary>
    public sealed class CSSUnparsedValue : IReadOnlyList<CSSUnparsedSegment>
    {
        private readonly List<CSSUnparsedSegment> _segments;
        public CSSUnparsedValue(IReadOnlyList<CSSUnparsedSegment> segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            _segments = new List<CSSUnparsedSegment>(segments.Count);
            for (int i = 0; i < segments.Count; i++) _segments.Add(segments[i]);
        }
        public int Count => _segments.Count;
        public int Length => Count;
        public CSSUnparsedSegment this[int index]
        {
            get => _segments[index];
            set { if (index == Count) _segments.Add(value); else _segments[index] = value; }
        }
        public IEnumerator<CSSUnparsedSegment> GetEnumerator() => _segments.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() { var text = new StringBuilder(); AppendTo(text, new HashSet<CSSUnparsedValue>()); return text.ToString(); }
        internal void AppendTo(StringBuilder text, HashSet<CSSUnparsedValue> path) => CssTokenSerializer.Append(text, ToTokens(path));
        internal List<ComponentValue> ToTokens(HashSet<CSSUnparsedValue> path)
        {
            if (!path.Add(this)) throw new DomTypeError("Cannot serialize cyclic unparsed variable fallback values.");
            try
            {
                var tokens = new List<ComponentValue>();
                foreach (var segment in _segments)
                    if (segment.Tokens != null) tokens.AddRange(segment.Tokens);
                    else if (segment.Variable != null) tokens.Add(segment.Variable.ToToken(path));
                    else throw new DomTypeError("An unparsed segment must contain a string or variable reference.");
                return tokens;
            }
            finally { path.Remove(this); }
        }
        internal static CSSUnparsedValue FromTokens(IReadOnlyList<ComponentValue> tokens)
        {
            var segments = new List<CSSUnparsedSegment>();
            var text = new StringBuilder();
            Extract(tokens, segments, text);
            if (text.Length > 0) segments.Add(text.ToString());
            return new CSSUnparsedValue(segments);
        }
        private static void Extract(IReadOnlyList<ComponentValue> tokens, List<CSSUnparsedSegment> segments, StringBuilder text)
        {
            CssToken? previous = null;
            foreach (var token in tokens)
            {
                if (previous.HasValue && CssTokenSerializer.Separate(previous.Value, CssTokenSerializer.First(token))) text.Append("/**/");
                if (token is FunctionValue variable && Names.AsciiEqual(variable.Name, "var")
                    && CustomVariable(variable, out string name, out var fallback))
                {
                    if (text.Length > 0) { segments.Add(text.ToString()); text.Clear(); }
                    segments.Add(new CSSVariableReferenceValue(name, fallback));
                }
                else if (token is FunctionValue function)
                {
                    text.Append(CssSerialization.SerializeIdentifier(function.Name)).Append('(');
                    Extract(function.Arguments, segments, text); text.Append(')');
                }
                else if (token is SimpleBlock block)
                {
                    text.Append(CssTokenSerializer.Serialize(block.OpenerToken));
                    Extract(block.Value, segments, text);
                    text.Append(block.Opener == CssTokenKind.LeftBrace ? '}' : block.Opener == CssTokenKind.LeftBracket ? ']' : ')');
                }
                else CssTokenSerializer.AppendOriginalValue(text, token);
                previous = token is PreservedToken preserved ? preserved.Token : null;
            }
        }
        private static bool CustomVariable(FunctionValue function, out string name, out CSSUnparsedValue? fallback)
        {
            int comma = -1;
            for (int i = 0; i < function.Arguments.Count; i++) if (function.Arguments[i] is PreservedToken { Token.Kind: CssTokenKind.Comma }) { comma = i; break; }
            var prefix = new List<ComponentValue>();
            for (int i = 0; i < (comma < 0 ? function.Arguments.Count : comma); i++) prefix.Add(function.Arguments[i]);
            if (!CssTypeParser.TryParse("custom-property-name", prefix, out var parsed) || parsed is not CssKeywordValue keyword)
            { name = ""; fallback = null; return false; }
            name = keyword.Value;
            fallback = null;
            if (comma >= 0)
            {
                var rest = new List<ComponentValue>();
                for (int i = comma + 1; i < function.Arguments.Count; i++) rest.Add(function.Arguments[i]);
                fallback = FromTokens(rest);
            }
            return true;
        }
    }
    /// <summary>CSS Typed OM 1 § 3.5 genuine variable reference, retaining fallback value identity.</summary>
    public sealed class CSSVariableReferenceValue
    {
        private string _variable;
        public CSSVariableReferenceValue(string variable, CSSUnparsedValue? fallback = null)
        { _variable = ""; Variable = variable; Fallback = fallback; }
        public string Variable
        {
            get => _variable;
            set
            {
                if (!CssTypeParser.TryParse("custom-property-name", CssParser.ParseComponentValueList(CssSerialization.SerializeIdentifier(value)), out _))
                    throw new DomTypeError("A variable reference requires a custom property name.");
                _variable = value;
            }
        }
        public CSSUnparsedValue? Fallback { get; set; }
        public override string ToString() { var text = new StringBuilder(); AppendTo(text, new HashSet<CSSUnparsedValue>()); return text.ToString(); }
        internal void AppendTo(StringBuilder text, HashSet<CSSUnparsedValue> path)
        {
            CssTokenSerializer.AppendOriginalValue(text, ToToken(path));
        }
        internal FunctionValue ToToken(HashSet<CSSUnparsedValue> path)
        {
            var arguments = new List<ComponentValue> { new PreservedToken(new CssToken(CssTokenKind.Ident, Variable)) };
            if (Fallback != null)
            {
                arguments.Add(new PreservedToken(new CssToken(CssTokenKind.Comma)));
                arguments.AddRange(Fallback.ToTokens(path));
            }
            return new FunctionValue(new CssToken(CssTokenKind.Function, "var"), arguments);
        }
    }
}
