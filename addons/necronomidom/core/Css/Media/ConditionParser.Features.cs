using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Media
{
    internal static partial class ConditionParser
    {
        private static QueryCondition? Feature(IReadOnlyList<ComponentValue> source)
        {
            var values = CssParser.Trim(source);
            if (values.Count == 1 && values[0] is PreservedToken boolean && boolean.Token.Kind == CssTokenKind.Ident)
                return new FeatureCondition(FeatureName(boolean.Token.Value));
            if (values.Count == 0) return null;
            var colon = -1;
            for (var i = 0; i < values.Count; i++) if (CssParser.Is(values[i], CssTokenKind.Colon)) { if (colon >= 0) return null; colon = i; }
            if (colon >= 0)
            {
                var name = CssParser.Trim(CssParser.Slice(values, 0, colon));
                var argument = CssParser.Trim(CssParser.Slice(values, colon + 1, values.Count - colon - 1));
                return Name(name) is string feature && FeatureValue(argument) ? new FeatureCondition(feature, FeatureText(argument)) : null;
            }
            var parts = new List<IReadOnlyList<ComponentValue>>();
            var operators = new List<string>();
            var start = 0;
            for (var i = 0; i < values.Count; i++)
            {
                if (!CssParser.IsDelim(values[i], '<') && !CssParser.IsDelim(values[i], '>') && !CssParser.IsDelim(values[i], '=')) continue;
                parts.Add(CssParser.Trim(CssParser.Slice(values, start, i - start)));
                var op = ((PreservedToken)values[i]).Token.Value;
                if (op != "=" && i + 1 < values.Count && CssParser.IsDelim(values[i + 1], '=')) { op += "="; i++; }
                operators.Add(op);
                start = i + 1;
            }
            parts.Add(CssParser.Trim(CssParser.Slice(values, start, values.Count - start)));
            if (operators.Count == 1)
            {
                var name = Name(parts[0]);
                if (name != null && FeatureValue(parts[1])) return new FeatureCondition(name, FeatureText(parts[1]), operators[0]);
                name = Name(parts[1]);
                if (name != null && FeatureValue(parts[0])) return new FeatureCondition(name, LeftValue: FeatureText(parts[0]), LeftComparison: operators[0]);
            }
            if (operators.Count == 2 && Name(parts[1]) is string middle && FeatureValue(parts[0]) && FeatureValue(parts[2]) &&
                operators[0][0] != '=' && operators[0][0] == operators[1][0])
                return new FeatureCondition(middle, FeatureText(parts[2]), operators[1], FeatureText(parts[0]), operators[0]);
            return null;
        }
        private static string? Name(IReadOnlyList<ComponentValue> values) => values.Count == 1 && values[0] is PreservedToken token && token.Token.Kind == CssTokenKind.Ident ? FeatureName(token.Token.Value) : null;
        private static string FeatureName(string name) => name.StartsWith("--", StringComparison.Ordinal) ? name : Dom.Names.AsciiLower(name);
        private static string FeatureText(IReadOnlyList<ComponentValue> values)
        {
            if (values.Count == 1 && values[0] is PreservedToken token && token.Token.Kind == CssTokenKind.Ident) return CssSerialization.SerializeIdentifier(Dom.Names.AsciiLower(token.Token.Value));
            return Values.CssValueParser.Parse(values)?.Serialize() ?? CssTokenSerializer.Serialize(values);
        }
        private static bool FeatureValue(IReadOnlyList<ComponentValue> values)
        {
            if (values.Count == 1)
            {
                if (values[0] is PreservedToken token && token.Token.Kind is CssTokenKind.Number or CssTokenKind.Dimension or CssTokenKind.Ident) return true;
                if (values[0] is FunctionValue function) return Values.Math.CssMathParser.TryParse(function, out _);
            }
            var compact = new List<ComponentValue>();
            foreach (var value in values) if (!CssParser.Is(value, CssTokenKind.Whitespace)) compact.Add(value);
            return compact.Count == 3 && compact[0] is PreservedToken numerator && numerator.Token.Kind == CssTokenKind.Number && numerator.Token.Number >= 0 &&
                CssParser.IsDelim(compact[1], '/') && compact[2] is PreservedToken denominator && denominator.Token.Kind == CssTokenKind.Number && denominator.Token.Number >= 0;
        }
    }
}
