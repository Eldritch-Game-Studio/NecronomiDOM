using NecronomiDom.Dom;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Math
{
    public static partial class CssMathParser
    {
        private static bool ParseFunction(FunctionValue function, CssNumericDimension? hint, int depth, out CssMathExpression? result)
        {
            result = null;
            if (depth > 128) return false;
            string name = Names.AsciiLower(function.Name);
            if (IsTreeCountingFunction(name)) return CssMathNumericLeaf.TryParse(function, hint, out result);
            if (!KnownFunction(name)) return false;
            var reader = new Reader(function.Arguments, hint, depth);
            if (name == "calc") return reader.Sum(out result) && result != null && reader.End;
            string? strategy = null;
            if (name == "round" && (reader.IsIdent("nearest") || reader.IsIdent("up") || reader.IsIdent("down") ||
                reader.IsIdent("to-zero") || reader.IsIdent("line-width")))
            {
                strategy = reader.TakeIdent();
                if (!reader.Take(CssTokenKind.Comma)) return false;
            }
            var arguments = new List<CssMathExpression>();
            var unbounded = new List<int>();
            do
            {
                if (name == "clamp" && arguments.Count != 1 && reader.IsIdent("none"))
                {
                    reader.TakeIdent(); unbounded.Add(arguments.Count);
                    arguments.Add(new CssMathLiteral(arguments.Count == 0 ? double.NegativeInfinity : double.PositiveInfinity, "", default));
                }
                else
                {
                    if (!reader.Sum(out CssMathExpression? argument) || argument == null) return false;
                    arguments.Add(argument);
                }
            } while (reader.Take(CssTokenKind.Comma));
            if (!reader.End || !ValidCount(name, arguments.Count)) return false;
            if (name == "round" && strategy == "line-width" && !arguments[0].Type.IsDimension(CssNumericDimension.Length)) return false;
            if (name == "round" && arguments.Count == 1 && strategy != "line-width")
            {
                if (!arguments[0].Type.MatchesNumber(hint ?? CssNumericDimension.Percentage)) return false;
                arguments.Add(new CssMathLiteral(1, "", default));
            }
            CssNumericType type = arguments[0].Type;
            if (name == "clamp" && unbounded.Contains(0)) type = arguments[1].Type;
            if (name is "min" or "max" or "clamp" or "round" or "mod" or "rem" or "hypot" or "atan2")
            {
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (unbounded.Contains(i)) continue;
                    if (!CssNumericType.Add(type, arguments[i].Type, out type)) return false;
                }
                foreach (int i in unbounded)
                    arguments[i] = new CssMathLiteral(i == 0 ? double.NegativeInfinity : double.PositiveInfinity, type.CanonicalUnit, type, true);
            }
            else if (name is "sin" or "cos" or "tan")
            {
                bool angle = type.SamePowers(CssNumericType.For(CssNumericDimension.Angle)) &&
                    (type.PercentageHint == null || hint.HasValue && type.PercentageHint == hint);
                if (!type.MatchesNumber(hint ?? CssNumericDimension.Percentage) && !angle) return false;
            }
            else if (name is "asin" or "acos" or "atan" or "pow" or "sqrt" or "log" or "exp")
            {
                foreach (CssMathExpression argument in arguments)
                    if (!argument.Type.MatchesNumber(hint ?? CssNumericDimension.Percentage)) return false;
            }
            CssNumericType outputType = name switch
            {
                "sin" or "cos" or "tan" or "pow" or "sqrt" or "log" or "exp" or "sign" => default,
                "asin" or "acos" or "atan" or "atan2" => CssNumericType.For(CssNumericDimension.Angle), _ => type
            };
            CssNumericDimension? outputHint = type.PercentageHint;
            foreach (CssMathExpression argument in arguments)
            {
                CssNumericDimension? argumentHint = argument.Type.PercentageHint;
                if (outputHint.HasValue && argumentHint.HasValue && outputHint != argumentHint) return false;
                outputHint ??= argumentHint;
            }
            outputType = outputType with { PercentageHint = outputHint };
            result = new CssMathOperation(name, arguments.ToArray(), outputType, strategy);
            return true;
        }
        public static bool IsMathFunction(string name)
        {
            name = Names.AsciiLower(name);
            return KnownFunction(name) || IsTreeCountingFunction(name);
        }
        private static bool IsTreeCountingFunction(string name) => name is "sibling-index" or "sibling-count";
        private static bool KnownFunction(string name) => name is "calc" or "min" or "max" or "clamp" or "round" or
            "mod" or "rem" or "abs" or "sign" or "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or
            "atan2" or "pow" or "sqrt" or "hypot" or "log" or "exp";
        private static bool ValidCount(string name, int count) => name switch
        {
            "min" or "max" or "hypot" => count >= 1,
            "clamp" => count == 3,
            "round" or "log" => count is 1 or 2,
            "mod" or "rem" or "atan2" or "pow" => count == 2, _ => count == 1
        };
    }
}
