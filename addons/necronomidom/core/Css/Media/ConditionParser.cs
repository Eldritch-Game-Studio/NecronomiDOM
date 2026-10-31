using System;
using System.Collections.Generic;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Media
{
    internal enum ConditionKind { Media, Supports, Container, Style }
    internal static partial class ConditionParser
    {
        internal static QueryCondition? Parse(IReadOnlyList<ComponentValue> source, ConditionKind kind, bool allowOr = true, SelectorParseOptions? selectorOptions = null)
        {
            var values = CssParser.Trim(source);
            if (values.Count == 0) return null;
            var p = 0;
            if (CssParser.IsIdent(values[0], "not"))
            {
                p++;
                Space(values, ref p);
                var inner = Atom(values, ref p, kind, selectorOptions);
                Space(values, ref p);
                return inner != null && p == values.Count ? new NotCondition(inner) : null;
            }
            var first = Atom(values, ref p, kind, selectorOptions);
            if (first == null) return null;
            var parts = new List<QueryCondition> { first };
            string? op = null;
            while (p < values.Count)
            {
                Space(values, ref p);
                if (p == values.Count) break;
                var next = CssParser.IsIdent(values[p], "and") ? "and" : CssParser.IsIdent(values[p], "or") && allowOr ? "or" : null;
                if (next == null || (op != null && op != next)) return null;
                op = next;
                p++;
                Space(values, ref p);
                var part = Atom(values, ref p, kind, selectorOptions);
                if (part == null) return null;
                parts.Add(part);
            }
            return op == null ? first : new JunctionCondition(op, parts);
        }
        private static QueryCondition? Atom(IReadOnlyList<ComponentValue> values, ref int p, ConditionKind kind, SelectorParseOptions? selectorOptions)
        {
            if (p >= values.Count) return null;
            var value = values[p++];
            if (value is FunctionValue function)
            {
                var name = Dom.Names.AsciiLower(function.Name);
                if (kind == ConditionKind.Supports && name == "selector")
                {
                    var selector = SelectorParser.ParseSelectorList(function.Arguments, selectorOptions ?? new SelectorParseOptions());
                    return selector == null ? new FunctionCondition(name, CssTokenSerializer.Serialize(function.Arguments).Trim()) : new SelectorCondition(selector);
                }
                if (kind == ConditionKind.Supports && name is "font-tech" or "font-format")
                {
                    var args = CssParser.Trim(function.Arguments);
                    return args.Count == 1 && args[0] is PreservedToken token && token.Token.Kind is CssTokenKind.Ident or CssTokenKind.String ? new FunctionCondition(name, CssTokenSerializer.Serialize(args)) : null;
                }
                if (kind == ConditionKind.Container && name == "style")
                {
                    var declaration = Declaration(function.Arguments);
                    var condition = declaration ?? Parse(function.Arguments, ConditionKind.Style);
                    if (condition == null && CssParser.Trim(function.Arguments) is var args && args.Count == 1 && args[0] is PreservedToken t && t.Token.Kind == CssTokenKind.Ident && t.Token.Value.StartsWith("--", StringComparison.Ordinal))
                        return new FunctionCondition(name, CssTokenSerializer.Serialize(args));
                    return condition == null ? null : new FunctionCondition(name, "", condition);
                }
                return ValidAny(function.Arguments) ? new GeneralEnclosedCondition(CssTokenSerializer.Serialize(function)) : null;
            }
            if (value is not SimpleBlock block || block.Opener != CssTokenKind.LeftParen) return null;
            var nested = Parse(block.Value, kind, selectorOptions: selectorOptions);
            if (nested != null) return new GroupCondition(nested);
            var leaf = kind is ConditionKind.Supports or ConditionKind.Style ? Declaration(block.Value) : Feature(block.Value);
            if (leaf != null) return leaf;
            var content = CssParser.Trim(block.Value);
            if (kind == ConditionKind.Style && content.Count == 1 && content[0] is PreservedToken { Token.Kind: CssTokenKind.Ident } property
                && property.Token.Value.StartsWith("--", StringComparison.Ordinal)) return new FeatureCondition(property.Token.Value);
            return content.Count > 0 && content[0] is PreservedToken ident && ident.Token.Kind == CssTokenKind.Ident && ValidAny(content) ? new GeneralEnclosedCondition(CssTokenSerializer.Serialize(block)) : null;
        }
        private static DeclarationCondition? Declaration(IReadOnlyList<ComponentValue> source)
        {
            var values = CssParser.Trim(source);
            if (values.Count < 2 || values[0] is not PreservedToken token || token.Token.Kind != CssTokenKind.Ident) return null;
            var p = 1;
            Space(values, ref p);
            if (p >= values.Count || !CssParser.Is(values[p++], CssTokenKind.Colon)) return null;
            var value = CssParser.Trim(CssParser.Slice(values, p, values.Count - p));
            if ((value.Count == 0 && !token.Token.Value.StartsWith("--", StringComparison.Ordinal)) || !ValidAny(value)) return null;
            foreach (var component in value) if (CssParser.Is(component, CssTokenKind.Semicolon) || CssParser.IsDelim(component, '!')) return null;
            var name = token.Token.Value.StartsWith("--", StringComparison.Ordinal) ? token.Token.Value : Dom.Names.AsciiLower(token.Token.Value);
            return new DeclarationCondition(name, CssTokenSerializer.Serialize(value));
        }
        private static bool Space(IReadOnlyList<ComponentValue> values, ref int p)
        { var start = p; while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++; return p != start; }
        private static bool ValidAny(IReadOnlyList<ComponentValue> values)
        {
            foreach (var value in values)
            {
                if (value is PreservedToken token && token.Token.Kind is CssTokenKind.BadString or CssTokenKind.BadUrl or CssTokenKind.RightParen or CssTokenKind.RightBracket or CssTokenKind.RightBrace) return false;
                if (value is FunctionValue function && !ValidAny(function.Arguments)) return false;
                if (value is SimpleBlock block && !ValidAny(block.Value)) return false;
            }
            return true;
        }
    }
}
