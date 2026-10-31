using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;

namespace NecronomiDom.Css.Values
{
    internal static partial class ShorthandExpansion
    {
        private static bool GridLines(PropertyDefinition definition, IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            List<List<ComponentValue>> parts = Split(tokens, false);
            var parsed = new List<CssValue>();
            foreach (List<ComponentValue> part in parts)
            {
                if (!CssTypeParser.TryParse("grid-line", part, out CssValue? line) || line == null) return false;
                parsed.Add(CssSpecifiedValueNormalizer.Normalize(definition.Longhands[parsed.Count], line));
            }
            if (definition.Name == "grid-area")
            {
                while (parsed.Count < 4)
                {
                    CssValue source = parsed[parsed.Count == 2 ? 0 : parsed.Count == 3 ? 1 : 0];
                    parsed.Add(IsCustomLine(source) ? source : Keyword("auto"));
                }
            }
            else if (parsed.Count == 1) parsed.Add(IsCustomLine(parsed[0]) ? parsed[0] : Keyword("auto"));
            for (int i = 0; i < definition.Longhands.Count; i++) values[definition.Longhands[i]] = parsed[i];
            return true;
        }
        private static bool IsCustomLine(CssValue value) => value is CssKeywordValue k && !k.Is("auto") && !k.Is("span");

        private static bool Grid(string name, IReadOnlyList<ComponentValue> tokens, GrammarMatch match, Dictionary<string, CssValue> values)
        {
            if (tokens.Count == 1 && tokens[0] is PreservedToken p && p.Token.IsIdent("none")) return true;
            if (name == "grid" && Capture(match, "'grid-template'") is CssValue template)
                return Put(values, "grid-template", template);
            List<List<ComponentValue>> parts = Split(tokens, false);
            if (name == "grid")
            {
                if (parts.Count != 2) return false;
                bool flowRows = ContainsIdent(parts[0], "auto-flow");
                List<ComponentValue> flow = parts[flowRows ? 0 : 1];
                bool dense = ContainsIdent(flow, "dense");
                var track = new List<ComponentValue>();
                foreach (ComponentValue token in flow)
                    if (token is not PreservedToken t || !t.Token.IsIdent("auto-flow") && !t.Token.IsIdent("dense")) track.Add(token);
                values["grid-auto-flow"] = dense ? List(new[] { Keyword(flowRows ? "row" : "column"), Keyword("dense") }) : Keyword(flowRows ? "row" : "column");
                if (track.Count != 0 && !ParsePut(values, flowRows ? "grid-auto-rows" : "grid-auto-columns", track)) return false;
                return ParsePut(values, flowRows ? "grid-template-columns" : "grid-template-rows", parts[flowRows ? 1 : 0]);
            }
            bool areas = false;
            foreach (ComponentValue token in parts[0]) if (token is PreservedToken { Token.Kind: CssTokenKind.String }) areas = true;
            if (!areas)
                return parts.Count == 2 && ParsePut(values, "grid-template-rows", parts[0]) && ParsePut(values, "grid-template-columns", parts[1]);
            var strings = new List<ComponentValue>();
            var rows = new List<ComponentValue>();
            IReadOnlyList<ComponentValue> source = TokenText.WithoutWhitespace(parts[0]);
            int index = 0;
            while (index < source.Count)
            {
                if (source[index] is SimpleBlock) AddGridNames(rows, source[index++]);
                if (index >= source.Count || source[index] is not PreservedToken { Token.Kind: CssTokenKind.String }) return false;
                strings.Add(source[index++]);
                if (index < source.Count && source[index] is not SimpleBlock && source[index] is not PreservedToken { Token.Kind: CssTokenKind.String })
                    rows.Add(source[index++]);
                else rows.Add(new PreservedToken(new CssToken(CssTokenKind.Ident, "auto")));
                if (index < source.Count && source[index] is SimpleBlock) AddGridNames(rows, source[index++]);
            }
            if (!CssValueConstraints.GridAreas(strings) || !ParsePut(values, "grid-template-areas", strings)
                || !ParsePut(values, "grid-template-rows", rows)) return false;
            return parts.Count == 1 || ParsePut(values, "grid-template-columns", parts[1]);
        }

        private static void AddGridNames(List<ComponentValue> rows, ComponentValue token)
        {
            if (token is SimpleBlock block && rows.Count != 0 && rows[^1] is SimpleBlock previous)
            {
                var names = new List<ComponentValue>(previous.Value.Count + block.Value.Count);
                names.AddRange(previous.Value);
                names.AddRange(block.Value);
                rows[^1] = new SimpleBlock(previous.OpenerToken, names);
            }
            else rows.Add(token);
        }

        private static bool ContainsIdent(IReadOnlyList<ComponentValue> tokens, string ident)
        {
            foreach (ComponentValue token in tokens) if (token is PreservedToken p && p.Token.IsIdent(ident)) return true;
            return false;
        }
    }
}
