using System.Text;

namespace NecronomiDom.Css.Values.Math
{
    internal static class CssMathSerialization
    {
        internal static bool IsArithmetic(string name) => name is "+" or "-" or "*" or "/" or "negate" or "invert";
        internal static void AppendOperation(StringBuilder builder, CssMathOperation operation, int parentPrecedence)
        {
            string name = operation.Name;
            if (name is "negate" or "invert")
            {
                bool nested = parentPrecedence != 0;
                if (nested) builder.Append('(');
                builder.Append(name == "negate" ? "-" : "1 / ");
                operation.Arguments[0].AppendTo(builder, 1);
                if (nested) builder.Append(')');
                return;
            }
            if (!IsArithmetic(name))
            {
                builder.Append(name).Append('(');
                if (name == "round")
                    builder.Append(operation.Strategy ?? "nearest").Append(", ");
                for (int i = 0; i < operation.Arguments.Count; i++)
                {
                    if (i != 0) builder.Append(", ");
                    operation.Arguments[i].AppendTo(builder, 0);
                }
                builder.Append(')'); return;
            }
            int precedence = name is "+" or "-" ? 1 : 2;
            bool wrap = parentPrecedence != 0;
            if (wrap) builder.Append('(');
            for (int i = 0; i < operation.Arguments.Count; i++)
            {
                CssMathExpression child = operation.Arguments[i];
                if (i != 0 && name == "+" && child is CssMathOperation { Name: "negate" } negation)
                {
                    builder.Append(" - ");
                    negation.Arguments[0].AppendTo(builder, 1);
                    continue;
                }
                if (i != 0 && name == "+" && child is CssMathOperation { Name: "*", Arguments.Count: >= 2 } product &&
                    product.Arguments[0] is CssMathLiteral { Unit: "" } factor && factor.Value < 0)
                {
                    builder.Append(" - ");
                    if (factor.Value == -1 && product.Arguments.Count == 2)
                        product.Arguments[1].AppendTo(builder, 1);
                    else
                    {
                        builder.Append('(');
                        CssMathLiteral.AppendNumber(builder, -factor.Value, "");
                        for (int j = 1; j < product.Arguments.Count; j++)
                        {
                            builder.Append(" * ");
                            product.Arguments[j].AppendTo(builder, 2);
                        }
                        builder.Append(')');
                    }
                    continue;
                }
                if (i != 0 && name == "*" && child is CssMathOperation { Name: "invert" } inversion)
                {
                    builder.Append(" / ");
                    inversion.Arguments[0].AppendTo(builder, 2);
                    continue;
                }
                if (i != 0 && name == "+" && child is CssMathLiteral negative && negative.Value < 0)
                {
                    builder.Append(" - ");
                    CssMathLiteral.AppendNumber(builder, -negative.Value, negative.Unit);
                    continue;
                }
                if (i != 0) builder.Append(' ').Append(name).Append(' ');
                child.AppendTo(builder, precedence + (i != 0 && (name is "-" or "/") ? 1 : 0));
            }
            if (wrap) builder.Append(')');
        }
    }
}
