using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    public readonly partial struct CSSNumericValue
    {
        /// <summary>Reifies the shared arithmetic graph into genuine Typed OM constructors. Matches Chrome 155 reification.</summary>
        public object Reify()
        {
            if (_parsedCalc) return new CSSMathSum(SumOperands(_expression!));
            if (TryGetUnit(out var unit)) return unit;
            var expression = CssMathSimplifier.Simplify(_expression!, CssMathEvaluationContext.Specified);
            if (expression is CssMathLiteral literal) return Unit(literal);
            if (expression is not CssMathOperation operation) return this;
            var arguments = operation.Arguments;
            switch (operation.Name)
            {
                case "+": case "-": return new CSSMathSum(SumOperands(expression));
                case "*": case "/": return new CSSMathProduct(ProductOperands(expression));
                case "negate": return new CSSMathNegate(new CSSNumericValue(arguments[0]));
                case "invert": return new CSSMathInvert(new CSSNumericValue(arguments[0]));
                case "min": return new CSSMathMin(Operands(arguments));
                case "max": return new CSSMathMax(Operands(arguments));
                case "clamp": return new CSSMathClamp(new CSSNumericValue(arguments[0]), new CSSNumericValue(arguments[1]), new CSSNumericValue(arguments[2]));
                default: return this;
            }
        }
        private static CSSUnitValue Unit(CssMathLiteral literal) => new(literal.Value,
            literal.Unit.Length == 0 ? "number" : literal.Unit == "%" ? "percent" : literal.Unit);
        private static bool ReifiableExpression(CssMathExpression expression)
        {
            if (expression is CssMathLiteral) return true;
            if (expression is not CssMathOperation operation || operation.Name is not ("+" or "-" or "*" or "/" or "negate" or "invert" or "min" or "max" or "clamp")) return false;
            for (int i = 0; i < operation.Arguments.Count; i++) if (!ReifiableExpression(operation.Arguments[i])) return false;
            return true;
        }
        private static bool ReifiableTokens(IReadOnlyList<ComponentValue> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i] is FunctionValue function)
                {
                    if (!Dom.Names.AsciiEqual(function.Name, "calc") && !Dom.Names.AsciiEqual(function.Name, "min")
                        && !Dom.Names.AsciiEqual(function.Name, "max") && !Dom.Names.AsciiEqual(function.Name, "clamp")) return false;
                    if (!ReifiableTokens(function.Arguments)) return false;
                }
                else if (tokens[i] is SimpleBlock block && !ReifiableTokens(block.Value)) return false;
            }
            return true;
        }
        private static CSSNumericValue[] Operands(IReadOnlyList<CssMathExpression> expressions)
        {
            var values = new CSSNumericValue[expressions.Count];
            for (int i = 0; i < values.Length; i++) values[i] = new CSSNumericValue(expressions[i]);
            return values;
        }
        private static List<CSSNumericValue> SumOperands(CssMathExpression expression)
        {
            var values = new List<CSSNumericValue>();
            CollectSum(expression, values);
            return values;
        }
        private static void CollectSum(CssMathExpression expression, List<CSSNumericValue> values)
        {
            if (expression is CssMathOperation { Name: "+" or "-" } operation)
            {
                CollectSum(operation.Arguments[0], values);
                for (int i = 1; i < operation.Arguments.Count; i++)
                    if (operation.Name == "-") values.Add(new CSSMathNegate(new CSSNumericValue(operation.Arguments[i])));
                    else CollectSum(operation.Arguments[i], values);
            }
            else values.Add(new CSSNumericValue(expression));
        }
        private static List<CSSNumericValue> ProductOperands(CssMathExpression expression)
        {
            var values = new List<CSSNumericValue>();
            CollectProduct(expression, values);
            return values;
        }
        private static void CollectProduct(CssMathExpression expression, List<CSSNumericValue> values)
        {
            if (expression is CssMathOperation { Name: "*" or "/" } operation)
            {
                CollectProduct(operation.Arguments[0], values);
                for (int i = 1; i < operation.Arguments.Count; i++)
                    if (operation.Name == "/") values.Add(new CSSMathInvert(new CSSNumericValue(operation.Arguments[i])));
                    else CollectProduct(operation.Arguments[i], values);
            }
            else values.Add(new CSSNumericValue(expression));
        }
    }
}
