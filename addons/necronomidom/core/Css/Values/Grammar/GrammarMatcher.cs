using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Grammar
{
    internal sealed record GrammarCapture(string Name, int Start, int End, CssValue Value);
    internal sealed record GrammarMatch(int End, CssValue Value, IReadOnlyList<GrammarCapture> Captures);

    /// <summary>Backtracking CSS Values 4 § 2 matcher; all alternatives retain their consumption positions.</summary>
    internal sealed partial class GrammarMatcher
    {
        private readonly IReadOnlyList<ComponentValue> _tokens;
        private readonly string _scope;
        private readonly string _property;
        internal GrammarMatcher(IReadOnlyList<ComponentValue> tokens, string scope)
        {
            _tokens = TokenText.WithoutWhitespace(tokens);
            _scope = scope;
            _property = scope;
        }

        private GrammarMatcher(GrammarMatcher parent, string scope)
        {
            _tokens = parent._tokens;
            _scope = scope;
            _property = parent._property;
        }

        internal GrammarMatch? Match(GrammarNode grammar)
        {
            foreach (GrammarMatch match in Walk(grammar, 0, 0))
                if (match.End == _tokens.Count) return match;
            return null;
        }

        private IEnumerable<GrammarMatch> Walk(GrammarNode node, int pos, int depth)
        {
            if (depth > 128) yield break;
            ComponentValue? token = pos < _tokens.Count ? _tokens[pos] : null;
            switch (node)
            {
                case KeywordNode keyword:
                    if (token is PreservedToken k && k.Token.IsIdent(keyword.Name))
                        yield return Leaf(pos + 1, new CssKeywordValue(keyword.Name));
                    break;
                case LiteralNode literal:
                    if (token is PreservedToken l && LiteralMatches(l.Token, literal.Text))
                        yield return Leaf(pos + 1, new CssDelimValue(literal.Text));
                    break;
                case TypeNode type:
                    foreach (GrammarMatch m in MatchType(type, pos, depth + 1)) yield return m;
                    break;
                case FunctionNode function:
                    if (token is FunctionValue f && NecronomiDom.Dom.Names.AsciiEqual(f.Name, function.Name))
                    {
                        GrammarMatch? body = new GrammarMatcher(f.Arguments, _scope).Match(function.Body);
                        if (body != null) yield return Leaf(pos + 1, new CssFunctionValue(function.Name, body.Value));
                    }
                    break;
                case BlockNode block:
                    if (token is SimpleBlock b && b.Opener == (block.Opener == '[' ? CssTokenKind.LeftBracket : CssTokenKind.LeftParen))
                    {
                        GrammarMatch? body = new GrammarMatcher(b.Value, _scope).Match(block.Body);
                        if (body != null) yield return Leaf(pos + 1, new CssBlockValue(body.Value, block.Opener));
                    }
                    break;
                case RequiredNode required:
                    foreach (GrammarMatch m in Walk(required.Child, pos, depth + 1))
                        if (m.End > pos) yield return m;
                    break;
                case MultiplierNode multiplier:
                    foreach (GrammarMatch m in Repeat(multiplier, pos, depth + 1, 0, new List<CssValue>(), new List<GrammarCapture>())) yield return m;
                    break;
                case CombinationNode combination:
                    if (combination.Kind == CombinatorKind.OneOf)
                    {
                        bool numericToken = token is PreservedToken { Token.Kind: CssTokenKind.Number };
                        if (numericToken)
                            foreach (GrammarNode child in combination.Items)
                                if (child is TypeNode { Name: "number" or "integer" or "zero" })
                                    foreach (GrammarMatch m in Walk(child, pos, depth + 1)) yield return m;
                        foreach (GrammarNode child in combination.Items)
                            if (!numericToken || child is not TypeNode { Name: "number" or "integer" or "zero" })
                                foreach (GrammarMatch m in Walk(child, pos, depth + 1)) yield return m;
                    }
                    else if (combination.Kind == CombinatorKind.Sequence)
                    {
                        foreach (GrammarMatch m in Sequence(combination.Items, 0, pos, depth + 1, new List<CssValue>(), new List<GrammarCapture>())) yield return m;
                    }
                    else
                    {
                        foreach (GrammarMatch m in Unordered(combination, pos, depth + 1, new bool[combination.Items.Count], new CssValue?[combination.Items.Count], new List<GrammarCapture>(), 0)) yield return m;
                    }
                    break;
            }
        }

        private static bool LiteralMatches(CssToken token, string text) => text switch
        {
            "," => token.Kind == CssTokenKind.Comma,
            ":" => token.Kind == CssTokenKind.Colon,
            ";" => token.Kind == CssTokenKind.Semicolon,
            _ => token.Kind == CssTokenKind.Delim && token.Value == text,
        };

        private static GrammarMatch Leaf(int end, CssValue value) => new(end, value, Array.Empty<GrammarCapture>());
        private static CssValue Pack(IReadOnlyList<CssValue> values, CssListSeparator separator = CssListSeparator.Space)
        {
            List<CssValue>? flat = null;
            for (int i = 0; i < values.Count; i++)
            {
                CssValue value = values[i];
                if (value is CssListValue list && list.Separator == separator)
                {
                    if (flat == null)
                    {
                        flat = new List<CssValue>();
                        for (int j = 0; j < i; j++) flat.Add(values[j]);
                    }
                    flat.AddRange(list.Items);
                }
                else flat?.Add(value);
            }
            if (flat != null) values = flat;
            if (separator != CssListSeparator.Space) return values.Count == 1 ? values[0] : new CssListValue(values, separator);
            for (int delimiterIndex = 0; delimiterIndex < 2; delimiterIndex++)
            {
                string delimiter = delimiterIndex == 0 ? "," : "/";
                if (values.Count > 0 && (values[0] is CssDelimValue first && first.Value == delimiter
                    || values[^1] is CssDelimValue last && last.Value == delimiter)) continue;
                var groups = new List<CssValue>();
                var current = new List<CssValue>();
                bool found = false;
                foreach (CssValue value in values)
                {
                    if (value is CssDelimValue d && d.Value == delimiter)
                    {
                        groups.Add(Pack(current));
                        current = new List<CssValue>();
                        found = true;
                    }
                    else current.Add(value);
                }
                if (found)
                {
                    groups.Add(Pack(current));
                    return new CssListValue(groups, delimiter == "," ? CssListSeparator.Comma : CssListSeparator.Slash);
                }
            }
            return values.Count == 1 ? values[0] : new CssListValue(values, separator);
        }

        private IEnumerable<GrammarMatch> Sequence(IReadOnlyList<GrammarNode> nodes, int index, int pos, int depth,
            List<CssValue> values, List<GrammarCapture> captures)
        {
            if (index == nodes.Count)
            {
                yield return new GrammarMatch(pos, Pack(values), captures);
                yield break;
            }
            foreach (GrammarMatch m in Walk(nodes[index], pos, depth + 1))
            {
                var next = new List<CssValue>(values);
                if (m.End > pos) next.Add(m.Value);
                var trace = new List<GrammarCapture>(captures);
                trace.AddRange(m.Captures);
                foreach (GrammarMatch result in Sequence(nodes, index + 1, m.End, depth, next, trace)) yield return result;
            }
            if (nodes[index] is LiteralNode { IsComma: true })
                foreach (GrammarMatch suffix in Sequence(nodes, index + 1, pos, depth, values, captures))
                    if (index > 0 && values.Count == 0 || index + 1 < nodes.Count && suffix.End == pos) yield return suffix;
        }

        private IEnumerable<GrammarMatch> Repeat(MultiplierNode node, int pos, int depth, int count,
            List<CssValue> values, List<GrammarCapture> captures)
        {
            if (count < node.Max)
            {
                int start = pos;
                if (count > 0 && node.CommaSeparated)
                {
                    if (pos >= _tokens.Count || _tokens[pos] is not PreservedToken { Token.Kind: CssTokenKind.Comma }) start = -1;
                    else start++;
                }
                if (start >= 0)
                    foreach (GrammarMatch m in Walk(node.Child, start, depth + 1))
                    {
                        if (m.End == start) continue;
                        var next = new List<CssValue>(values) { m.Value };
                        var trace = new List<GrammarCapture>(captures);
                        trace.AddRange(m.Captures);
                        foreach (GrammarMatch result in Repeat(node, m.End, depth, count + 1, next, trace)) yield return result;
                    }
            }
            if (count >= node.Min)
                yield return new GrammarMatch(pos, Pack(values, node.CommaSeparated ? CssListSeparator.Comma : CssListSeparator.Space), captures);
        }

        private IEnumerable<GrammarMatch> Unordered(CombinationNode node, int pos, int depth, bool[] used,
            CssValue?[] values, List<GrammarCapture> captures, int count)
        {
            for (int i = 0; i < used.Length; i++)
            {
                if (used[i]) continue;
                foreach (GrammarMatch m in Walk(node.Items[i], pos, depth + 1))
                {
                    if (m.End == pos) continue;
                    var taken = (bool[])used.Clone();
                    taken[i] = true;
                    var next = (CssValue?[])values.Clone();
                    next[i] = m.End > pos ? m.Value : null;
                    var trace = new List<GrammarCapture>(captures);
                    trace.AddRange(m.Captures);
                    foreach (GrammarMatch result in Unordered(node, m.End, depth, taken, next, trace, count + 1)) yield return result;
                }
            }
            if (node.Kind == CombinatorKind.AllOf ? Complete(node, used, pos, depth) : count > 0)
            {
                var output = new List<CssValue>();
                foreach (CssValue? value in values) if (value != null) output.Add(value);
                yield return new GrammarMatch(pos, Pack(output), captures);
            }
        }

        private bool Complete(CombinationNode node, bool[] used, int pos, int depth)
        {
            for (int i = 0; i < used.Length; i++)
            {
                if (used[i]) continue;
                bool empty = false;
                foreach (GrammarMatch match in Walk(node.Items[i], pos, depth + 1))
                    if (match.End == pos) { empty = true; break; }
                if (!empty) return false;
            }
            return true;
        }
    }
}
