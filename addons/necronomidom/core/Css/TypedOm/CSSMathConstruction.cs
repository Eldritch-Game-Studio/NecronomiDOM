using System;
using System.Text;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    internal static class CSSMathConstruction
    {
        internal static CSSNumericArray Numbers(double first, double[] rest)
        {
            ArgumentNullException.ThrowIfNull(rest);
            var values = new CSSNumericValue[rest.Length + 1];
            values[0] = new CSSUnitValue(first, "number");
            for (int i = 0; i < rest.Length; i++) values[i + 1] = new CSSUnitValue(rest[i], "number");
            return new CSSNumericArray(values, true);
        }
        internal static CSSNumericValue Create(string operation, CSSNumericArray values)
        {
            if (values.Count == 0) throw DomException.Syntax("A CSS math operation requires at least one operand.");
            var type = values[0].Type;
            var arguments = new CssMathExpression[values.Count];
            arguments[0] = values[0].Expression;
            for (int i = 1; i < values.Count; i++)
            {
                bool compatible = operation == "*" ? CssNumericType.Multiply(type, values[i].Type, false, out var next)
                    : CssNumericType.Add(type, values[i].Type, out next);
                if (!compatible) throw new DomTypeError("CSS numeric dimensions are incompatible.");
                type = next;
                arguments[i] = values[i].Expression;
            }
            return new CSSNumericValue(new CssMathOperation(operation, arguments, type));
        }
        internal static CSSNumericValue Unary(string operation, CSSNumericValue value)
        {
            var type = value.Type;
            if (operation == "invert" && !CssNumericType.Multiply(default, type, true, out type))
                throw new DomTypeError("CSS numeric dimensions are incompatible.");
            return new CSSNumericValue(new CssMathOperation(operation, new[] { value.Expression }, type));
        }
        internal static string Serialize(CSSNumericValue value, bool function = false)
        {
            var builder = new StringBuilder();
            if (!function) builder.Append("calc(");
            value.Expression.AppendTo(builder, 0);
            if (!function) builder.Append(')');
            return builder.ToString();
        }
    }
}
