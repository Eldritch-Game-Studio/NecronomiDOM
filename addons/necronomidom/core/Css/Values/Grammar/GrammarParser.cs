using System;
using System.Collections.Generic;
using System.Globalization;

namespace NecronomiDom.Css.Values.Grammar
{
    /// <summary>
    /// Parses CSS Values 4 § 2 value definition syntax (as published by webref) into <see cref="GrammarNode"/>s.
    /// Precedence (§ 2.2): juxtaposition &gt; <c>&amp;&amp;</c> &gt; <c>||</c> &gt; <c>|</c>; multipliers bind to the
    /// preceding term. Quoted <c>'['</c> … <c>']'</c> pairs become <see cref="BlockNode"/>s.
    /// </summary>
    internal sealed partial class GrammarParser
    {
        private readonly List<GrammarToken> _tokens;
        private int _pos;

        private GrammarParser(List<GrammarToken> tokens) => _tokens = tokens;

        /// <summary>Parses <paramref name="syntax"/>; throws <see cref="FormatException"/> on malformed input.</summary>
        public static GrammarNode Parse(string syntax)
        {
            var parser = new GrammarParser(Tokenize(syntax));
            GrammarNode node = parser.ParseOneOf();
            if (parser._pos != parser._tokens.Count) throw new FormatException("unexpected '" + parser._tokens[parser._pos].Text + "' in " + syntax);
            return node;
        }

        private GrammarToken? Peek => _pos < _tokens.Count ? _tokens[_pos] : null;

        private GrammarNode ParseOneOf() => ParseCombination(CombinatorKind.OneOf, "|", ParseAnyOf);

        private GrammarNode ParseAnyOf() => ParseCombination(CombinatorKind.AnyOf, "||", ParseAllOf);

        private GrammarNode ParseAllOf() => ParseCombination(CombinatorKind.AllOf, "&&", ParseSequence);

        private GrammarNode ParseCombination(CombinatorKind kind, string op, Func<GrammarNode> next)
        {
            var items = new List<GrammarNode> { next() };
            while (Peek is { Kind: GrammarTokenKind.Combinator } t && t.Text == op)
            {
                _pos++;
                items.Add(next());
            }

            return items.Count == 1 ? items[0] : new CombinationNode(kind, items);
        }

        private GrammarNode ParseSequence()
        {
            var items = new List<GrammarNode>();
            while (Peek is GrammarToken t && t.Kind != GrammarTokenKind.Combinator && t.Kind != GrammarTokenKind.CloseBracket
                && t.Kind != GrammarTokenKind.CloseParen && !(t.Kind == GrammarTokenKind.Quoted && t.Text is "]" or ")"))
            {
                items.Add(ParseTerm());
            }

            if (items.Count == 0) return new CombinationNode(CombinatorKind.Sequence, items);
            return items.Count == 1 ? items[0] : new CombinationNode(CombinatorKind.Sequence, items);
        }

        private GrammarNode ParseTerm()
        {
            GrammarNode node = ParsePrimary();
            while (Peek is { Kind: GrammarTokenKind.Multiplier } m)
            {
                _pos++;
                node = ApplyMultiplier(node, m.Text);
            }

            return node;
        }

        private static GrammarNode ApplyMultiplier(GrammarNode node, string text)
        {
            switch (text)
            {
                case "*": return new MultiplierNode(node, 0, int.MaxValue, false);
                case "+": return new MultiplierNode(node, 1, int.MaxValue, false);
                case "?": return new MultiplierNode(node, 0, 1, false);
                case "#": return new MultiplierNode(node, 1, int.MaxValue, true);
                case "!": return new RequiredNode(node);
            }

            // {A}, {A,}, {A,B}; a range directly after '#' refines the comma repetition.
            string body = text.Substring(1, text.Length - 2);
            int comma = body.IndexOf(',');
            int min = int.Parse(comma < 0 ? body : body[..comma], CultureInfo.InvariantCulture);
            int max = comma < 0 ? min : comma == body.Length - 1 ? int.MaxValue : int.Parse(body[(comma + 1)..], CultureInfo.InvariantCulture);
            if (min < 0 || max < min) throw new FormatException("invalid multiplier range " + text);
            if (node is MultiplierNode { CommaSeparated: true, Min: 1, Max: int.MaxValue } hash) return new MultiplierNode(hash.Child, min, max, true);
            return new MultiplierNode(node, min, max, false);
        }

        private GrammarNode ParsePrimary()
        {
            GrammarToken t = Peek ?? throw new FormatException("unexpected end of syntax");
            _pos++;
            switch (t.Kind)
            {
                case GrammarTokenKind.OpenBracket:
                {
                    GrammarNode inner = ParseOneOf();
                    Expect(GrammarTokenKind.CloseBracket, "]");
                    return inner;
                }

                case GrammarTokenKind.OpenParen:
                {
                    GrammarNode inner = ParseOneOf();
                    Expect(GrammarTokenKind.CloseParen, ")");
                    return new BlockNode(inner, '(');
                }

                case GrammarTokenKind.Function:
                {
                    GrammarNode body = Peek is { Kind: GrammarTokenKind.CloseParen } ? new CombinationNode(CombinatorKind.Sequence, Array.Empty<GrammarNode>()) : ParseOneOf();
                    Expect(GrammarTokenKind.CloseParen, ")");
                    return new FunctionNode(t.Text, body);
                }

                case GrammarTokenKind.Quoted when t.Text is "[" or "(":
                {
                    GrammarNode inner = ParseOneOf();
                    string closer = t.Text == "[" ? "]" : ")";
                    if (Peek is not GrammarToken closing || closing.Kind != GrammarTokenKind.Quoted || closing.Text != closer) throw new FormatException("missing '" + closer + "' literal");
                    _pos++;
                    return new BlockNode(inner, t.Text[0]);
                }

                case GrammarTokenKind.Type: return ParseType(t.Text);
                case GrammarTokenKind.Keyword: return new KeywordNode(NecronomiDom.Dom.Names.AsciiLower(t.Text));
                case GrammarTokenKind.Quoted:
                case GrammarTokenKind.Literal:
                    return new LiteralNode(t.Text);
                default:
                    throw new FormatException("unexpected '" + t.Text + "'");
            }
        }

        private void Expect(GrammarTokenKind kind, string text)
        {
            if (Peek is not GrammarToken t || t.Kind != kind) throw new FormatException("expected '" + text + "'");
            _pos++;
        }

        /// <summary>Parses the inside of <c>&lt;…&gt;</c>: <c>'prop'</c>, <c>name()</c>, <c>name [min,max]</c>, <c>name[ … ]</c>.</summary>
        private static GrammarNode ParseType(string inner)
        {
            inner = inner.Trim();
            if (inner.Length > 1 && inner[0] == '\'' && inner[^1] == '\'') return new TypeNode(inner[1..^1], true, double.NegativeInfinity, double.PositiveInfinity);
            int bracket = inner.IndexOf('[');
            if (bracket < 0) return new TypeNode(inner, false, double.NegativeInfinity, double.PositiveInfinity);
            string name = inner[..bracket].Trim();
            if (bracket > 0 && inner[bracket - 1] != ' ') return new NeverNode("parametric type " + inner);
            string range = inner[(bracket + 1)..inner.LastIndexOf(']')];
            string[] parts = range.Split(',');
            if (parts.Length != 2) return new TypeNode(name, false, double.NegativeInfinity, double.PositiveInfinity);
            return new TypeNode(name, false, ParseBound(parts[0]), ParseBound(parts[1]));
        }

        private static double ParseBound(string text)
        {
            text = text.Trim();
            if (text == "∞" || text == "+∞") return double.PositiveInfinity;
            if (text == "-∞" || text == "−∞") return double.NegativeInfinity;
            int end = 0;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == '-' || text[end] == '+')) end++;
            return CssNumericRange.Canonical(double.Parse(text[..end], CultureInfo.InvariantCulture), text[end..]);
        }
    }
}
