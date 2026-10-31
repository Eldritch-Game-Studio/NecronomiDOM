using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Media
{
    public static partial class MediaEvaluator
    {
        private static bool EvaluateStyle(FunctionCondition function, MediaEnvironment environment)
        {
            if (environment.ContainerStyle == null) return false;
            if (function.Condition != null) return EvaluateStyleCondition(function.Condition, environment);
            return environment.ContainerStyle(function.Argument.Trim()) != null;
        }
        private static bool EvaluateStyleCondition(QueryCondition condition, MediaEnvironment environment)
        {
            switch (condition)
            {
                case GroupCondition group: return EvaluateStyleCondition(group.Condition, environment);
                case NotCondition not: return !EvaluateStyleCondition(not.Condition, environment);
                case JunctionCondition junction:
                    bool and = junction.Operator == "and";
                    for (int i = 0; i < junction.Conditions.Count; i++)
                        if (EvaluateStyleCondition(junction.Conditions[i], environment) != and) return !and;
                    return and;
                case FeatureCondition { Value: null } boolean when boolean.Name.StartsWith("--", StringComparison.Ordinal):
                    return environment.ContainerStyle!(boolean.Name) != null;
                case DeclarationCondition declaration:
                    string? actual = environment.ContainerStyle!(declaration.Property);
                    return actual != null && EqualStyleTokens(CssParser.ParseComponentValueList(actual), CssParser.ParseComponentValueList(declaration.Value));
                default: return false;
            }
        }
        private static bool EqualStyleTokens(IReadOnlyList<ComponentValue> left, IReadOnlyList<ComponentValue> right)
        {
            int a = 0, b = 0;
            while (true)
            {
                while (a < left.Count && CssParser.Is(left[a], CssTokenKind.Whitespace)) a++;
                while (b < right.Count && CssParser.Is(right[b], CssTokenKind.Whitespace)) b++;
                if (a == left.Count || b == right.Count) return a == left.Count && b == right.Count;
                if (!EqualStyleToken(left[a++], right[b++])) return false;
            }
        }
        private static bool EqualStyleToken(ComponentValue left, ComponentValue right)
        {
            if (left is PreservedToken a && right is PreservedToken b)
            {
                if (a.Token.Kind != b.Token.Kind) return false;
                if (a.Token.Kind is CssTokenKind.Number or CssTokenKind.Percentage or CssTokenKind.Dimension)
                    return a.Token.Number == b.Token.Number && Names.AsciiEqual(a.Token.Unit, b.Token.Unit);
                return a.Token.Value == b.Token.Value;
            }
            if (left is FunctionValue first && right is FunctionValue second)
                return first.Name == second.Name && EqualStyleTokens(first.Arguments, second.Arguments);
            return left is SimpleBlock x && right is SimpleBlock y && x.Opener == y.Opener && EqualStyleTokens(x.Value, y.Value);
        }
    }
}
