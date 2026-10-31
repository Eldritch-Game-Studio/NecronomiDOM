using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Media
{
    /// <summary>Media Queries 4/5 § 3 media query list parsing and invalid-query recovery.</summary>
    public static class MediaQueryParser
    {
        public static MediaQueryList Parse(string css) => Parse(CssParser.ParseComponentValueList(css));
        public static MediaQueryList Parse(IReadOnlyList<ComponentValue> source)
        {
            if (CssParser.Trim(source).Count == 0) return new MediaQueryList(Array.Empty<MediaQuery>());
            var result = new List<MediaQuery>();
            foreach (var entry in CssParser.SplitComma(source)) result.Add(ParseQuery(CssParser.Trim(entry)) ?? new MediaQuery(IsValid: false));
            return new MediaQueryList(result);
        }
        private static MediaQuery? ParseQuery(IReadOnlyList<ComponentValue> values)
        {
            var condition = ConditionParser.Parse(values, ConditionKind.Media);
            if (condition != null) return new MediaQuery(Condition: condition);
            var p = 0;
            string? modifier = null;
            if (values.Count == 0) return null;
            if (CssParser.IsIdent(values[p], "not") || CssParser.IsIdent(values[p], "only"))
            {
                modifier = Dom.Names.AsciiLower(((PreservedToken)values[p++]).Token.Value);
                while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++;
            }
            if (p >= values.Count || values[p] is not PreservedToken type || type.Token.Kind != CssTokenKind.Ident) return null;
            var name = Dom.Names.AsciiLower(type.Token.Value);
            if (name is "not" or "only" or "and" or "or" or "layer") return null;
            p++;
            if (p == values.Count) return new MediaQuery(name, modifier);
            while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++;
            if (p == values.Count) return new MediaQuery(name, modifier);
            if (!CssParser.IsIdent(values[p++], "and")) return null;
            condition = ConditionParser.Parse(CssParser.Slice(values, p, values.Count - p), ConditionKind.Media, false);
            return condition == null ? null : new MediaQuery(name, modifier, condition);
        }
    }
    /// <summary>CSS Conditional 3/4 @supports condition grammar.</summary>
    public static class SupportsParser
    {
        public static QueryCondition? Parse(string css, Selectors.SelectorParseOptions? selectorOptions = null)
        {
            var values = CssParser.ParseComponentValueList(css, null, "", out var source);
            return Parse(values, (selectorOptions ?? new Selectors.SelectorParseOptions()) with { SourceText = source });
        }
        public static QueryCondition? Parse(IReadOnlyList<ComponentValue> values, Selectors.SelectorParseOptions? selectorOptions = null) => ConditionParser.Parse(values, ConditionKind.Supports, selectorOptions: selectorOptions);
    }
    /// <summary>CSS Conditional 5 size/style container query grammar.</summary>
    public static class ContainerQueryParser
    {
        public static ContainerQuery? Parse(string css) => Parse(CssParser.ParseComponentValueList(css));
        public static ContainerQuery? Parse(IReadOnlyList<ComponentValue> source)
        {
            var values = CssParser.Trim(source);
            string? name = null;
            if (values.Count > 0 && values[0] is PreservedToken token && token.Token.Kind == CssTokenKind.Ident && !token.Token.IsIdent("not"))
            {
                name = token.Token.Value;
                if (Dom.Names.AsciiLower(name) is "none" or "and" or "or" or "default" or "initial" or "inherit" or "unset" or "revert" or "revert-layer") return null;
                values = CssParser.Trim(CssParser.Slice(values, 1, values.Count - 1));
            }
            var condition = ConditionParser.Parse(values, ConditionKind.Container);
            return condition == null ? null : new ContainerQuery(name, condition);
        }
    }
}
