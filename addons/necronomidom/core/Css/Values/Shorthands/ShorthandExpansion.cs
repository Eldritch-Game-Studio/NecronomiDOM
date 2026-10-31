using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;

namespace NecronomiDom.Css.Values
{
    /// <summary>Shorthand expansion to leaf properties (CSS Cascade 5 § 3 and property-specific definitions).</summary>
    internal static partial class ShorthandExpansion
    {
        private static readonly ConcurrentDictionary<string, CssValue> InitialValues = new(System.StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<PropertyDefinition, string[]> LeafLists = new();
        /// <summary>Leaf longhands in expansion order; immutable metadata computed once per definition.</summary>
        internal static IReadOnlyList<string> Leaves(PropertyDefinition definition) => LeafLists.GetOrAdd(definition, static definition =>
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var leaves = new List<string>();
            foreach (string name in Children(definition))
                foreach (string leaf in LeafNames(name)) if (seen.Add(leaf)) leaves.Add(leaf);
            return leaves.ToArray();
        });
        private static IEnumerable<string> Children(PropertyDefinition definition)
        {
            foreach (string name in definition.Longhands) yield return name;
            foreach (string name in definition.ResetLonghands) yield return name;
            if (definition.Name == "font-synthesis") yield return "font-synthesis-position";
            if (definition.Name == "white-space") yield return "white-space-trim";
        }
        private static IEnumerable<string> LeafNames(string name)
        {
            PropertyDefinition? definition = CssPropertyRegistry.Get(name);
            if (definition == null) yield break;
            if (!definition.IsShorthand) yield return definition.Name;
            else foreach (string child in Leaves(definition)) yield return child;
        }

        internal static ParsedProperty? Expand(PropertyDefinition definition, IReadOnlyList<ComponentValue> tokens, GrammarMatch match)
        {
            var values = new Dictionary<string, CssValue>(StringComparer.Ordinal);
            foreach (string name in Leaves(definition))
            {
                CssValue? initial = Initial(name);
                if (initial == null) return null;
                values[name] = initial;
            }
            bool valid = definition.Name switch
            {
                "flex" => Flex(tokens, values),
                "animation-range" => RangeLayers(tokens, values),
                "font" => Font(tokens, match, values),
                "font-synthesis" => FontSynthesis(tokens, values),
                "font-variant" => FontVariant(tokens, values),
                "white-space" => WhiteSpace(tokens, values),
                "text-box" => TextBox(match, values),
                "vertical-align" => VerticalAlign(tokens, values),
                "contain-intrinsic-size" => IntrinsicSize(tokens, values),
                "grid" or "grid-template" => Grid(definition.Name, tokens, match, values),
                "grid-area" or "grid-row" or "grid-column" => GridLines(definition, tokens, values),
                "background" or "mask" => Layers(definition, tokens, values),
                "animation" or "transition" => Motion(definition, tokens, values),
                "background-position" => PositionLayers(tokens, values),
                "border-radius" => Radius(definition, tokens, values),
                "border-block-start-radius" or "border-block-end-radius" or "border-inline-start-radius" or "border-inline-end-radius" => Radius(definition, tokens, values),
                "border" or "border-top" or "border-right" or "border-bottom" or "border-left" or "border-block" or "border-inline" or "border-block-start" or "border-block-end" or "border-inline-start" or "border-inline-end" or "-webkit-text-stroke" => Border(definition, match, values),
                _ => Generic(definition, tokens, match, values),
            };
            if (!valid) return null;
            var result = new List<(string, CssValue)>(values.Count);
            foreach (var entry in values) result.Add((entry.Key, entry.Value));
            return new ParsedProperty(result);
        }

        internal static CssValue? Initial(string name)
        {
            if (InitialValues.TryGetValue(name, out CssValue? cached)) return cached;
            PropertyDefinition? definition = CssPropertyRegistry.Get(name);
            if (definition == null || definition.Initial.Length == 0) return null;
            ParsedProperty? parsed = CssPropertyParser.Parse(name, CssParser.ParseComponentValueList(definition.Initial));
            if (parsed == null) return null;
            foreach (var pair in parsed.Longhands)
                if (pair.Longhand == definition.Name) { InitialValues.TryAdd(name, pair.Value); return pair.Value; }
            return null;
        }
        private static bool Put(Dictionary<string, CssValue> values, string name, CssValue value)
        {
            PropertyDefinition? definition = CssPropertyRegistry.Get(name);
            if (definition == null) return true;
            if (!definition.IsShorthand) { values[definition.Name] = CssSpecifiedValueNormalizer.Normalize(definition.Name, value); return true; }
            ParsedProperty? expanded = CssPropertyParser.Parse(name, CssParser.ParseComponentValueList(value.Serialize()));
            if (expanded == null) return false;
            foreach (var item in expanded.Longhands) values[item.Longhand] = item.Value;
            return true;
        }
        private static bool ParsePut(Dictionary<string, CssValue> values, string name, IReadOnlyList<ComponentValue> tokens)
        {
            ParsedProperty? parsed = CssPropertyParser.Parse(name, tokens);
            if (parsed == null) return false;
            foreach (var item in parsed.Longhands) values[item.Longhand] = item.Value;
            return true;
        }
        private static CssValue? Capture(GrammarMatch match, string name)
        {
            foreach (GrammarCapture capture in match.Captures) if (capture.Name == name) return capture.Value;
            return null;
        }
        private static List<List<ComponentValue>> Split(IReadOnlyList<ComponentValue> tokens, bool comma)
        {
            var result = new List<List<ComponentValue>> { new() };
            foreach (ComponentValue token in tokens)
            {
                if (token is PreservedToken p && (comma ? p.Token.Kind == CssTokenKind.Comma : p.Token.IsDelim('/'))) result.Add(new List<ComponentValue>());
                else result[^1].Add(token);
            }
            return result;
        }
        private static CssValue Keyword(string name) => new CssKeywordValue(name);
        private static CssValue List(IReadOnlyList<CssValue> values, CssListSeparator separator = CssListSeparator.Space)
            => values.Count == 1 ? values[0] : new CssListValue(values, separator);
    }
}
