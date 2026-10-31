using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorParser
    {
        private static bool ValidAnyValue(IReadOnlyList<ComponentValue> values)
        {
            foreach (var value in values)
            {
                if (value is PreservedToken token && token.Token.Kind is CssTokenKind.BadString or CssTokenKind.BadUrl or CssTokenKind.RightParen or CssTokenKind.RightBracket or CssTokenKind.RightBrace) return false;
                if (value is FunctionValue function && !ValidAnyValue(function.Arguments)) return false;
                if (value is SimpleBlock block && !ValidAnyValue(block.Value)) return false;
            }
            return true;
        }
        private static string InvalidText(IReadOnlyList<ComponentValue> values, string? source)
        {
            if (values.Count == 0) return "";
            var start = values[0] switch
            {
                PreservedToken token => token.Token.Offset,
                FunctionValue function => function.NameToken.Offset,
                SimpleBlock block => block.OpenerToken.Offset,
                _ => -1,
            };
            var end = values[values.Count - 1].EndOffset;
            return source != null && start >= 0 && end >= start && end <= source.Length ? source.Substring(start, end - start).Trim() : CssTokenSerializer.Serialize(values);
        }
        private static bool ContainsNesting(IReadOnlyList<ComponentValue> values)
        {
            foreach (var value in values)
            {
                if (CssParser.IsDelim(value, '&')) return true;
                if (value is FunctionValue function && ContainsNesting(function.Arguments)) return true;
                if (value is SimpleBlock block && ContainsNesting(block.Value)) return true;
            }
            return false;
        }
        private static bool IsPartSelector(ComplexSelector selector)
        {
            if (selector.Compounds.Count != 1) return false;
            foreach (var simple in selector.Compounds[0].SimpleSelectors)
                if (simple is not PseudoClassSelector state || !IsPartState(state)) return false;
            return true;
        }
        private static bool IsPartState(PseudoClassSelector state) => state.Name is not
            ("has" or "host" or "host-context" or "root" or "scope" or "empty" or "first-child" or "last-child" or "only-child" or "first-of-type" or "last-of-type" or "only-of-type" or "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type");
    }
}
