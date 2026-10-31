using System.Collections.Generic;

namespace NecronomiDom.Css.Values.Math
{
    internal static partial class CssMathSimplifier
    {
        private static CssMathExpression Product(List<CssMathExpression> children, CssNumericType type, CssMathEvaluationContext context, bool computed = false)
        {
            Flatten(children, "*");
            double scalar = 1;
            int scalarCount = 0;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not CssMathLiteral { Unit: "" } number) continue;
                scalar *= number.Value; scalarCount++;
                children.RemoveAt(i--);
            }
            if (scalarCount != 0) children.Insert(0, new CssMathLiteral(scalar, "", default));
            var literals = new List<CssMathExpression>();
            var others = new List<CssMathExpression>();
            foreach (CssMathExpression child in children)
                if (child is CssMathLiteral) literals.Add(child); else others.Add(child);
            literals.Sort(CompareTerms);
            children.Clear(); children.AddRange(literals); children.AddRange(others);
            if (children.Count == 2 && children[0] is CssMathLiteral { Unit: "" } scale)
            {
                if (children[1] is CssMathLiteral quantity)
                {
                    double magnitude = scale.Value * quantity.Value;
                    string resultUnit = computed && (double.IsNaN(magnitude) || magnitude == 0 && quantity.Unit != "%")
                        ? type.CanonicalUnit : quantity.Unit;
                    return new CssMathLiteral(magnitude, resultUnit, type);
                }
                if (children[1] is CssMathOperation { Name: "+" } sum)
                {
                    bool allLiterals = true;
                    foreach (CssMathExpression term in sum.Arguments) if (term is not CssMathLiteral) allLiterals = false;
                    if (allLiterals)
                    {
                        var terms = new List<CssMathExpression>();
                        foreach (CssMathExpression term in sum.Arguments)
                        {
                            var literal = (CssMathLiteral)term;
                            terms.Add(new CssMathLiteral(scale.Value * literal.Value, literal.Unit, literal.Type));
                        }
                        return Sum(terms, type, computed);
                    }
                }
            }
            var result = children.Count == 1 ? children[0] : new CssMathOperation("*", children.ToArray(), type);
            if (type.IsSimple && TryLiteralAlgebra(result, out double value, out string unit))
                return new CssMathLiteral(value, unit, type);
            bool canEvaluate = type.IsSimple && CanEvaluate(children, context);
            if (type.IsSimple && (canEvaluate || computed) && result.TryEvaluate(context, out value) &&
                (canEvaluate || double.IsNaN(value)))
                return new CssMathLiteral(value, type.CanonicalUnit, type);
            return result;
        }
        private static bool TryLiteralAlgebra(CssMathExpression expression, out double value, out string unit)
        {
            var powers = new Dictionary<string, int>(System.StringComparer.Ordinal);
            value = 1; unit = "";
            if (!Gather(expression, false, powers, ref value)) return false;
            foreach (KeyValuePair<string, int> pair in powers)
            {
                if (pair.Value == 0) continue;
                if (pair.Value != 1 || unit.Length != 0) return false;
                unit = pair.Key;
            }
            return true;
        }
        private static bool Gather(CssMathExpression expression, bool invert, Dictionary<string, int> powers, ref double value)
        {
            if (expression is CssMathLiteral literal)
            {
                value = invert ? value / literal.Value : value * literal.Value;
                if (literal.Unit.Length != 0)
                {
                    powers.TryGetValue(literal.Unit, out int exponent);
                    powers[literal.Unit] = exponent + (invert ? -1 : 1);
                }
                return true;
            }
            if (expression is not CssMathOperation op || op.Name is not ("*" or "/")) return false;
            for (int i = 0; i < op.Arguments.Count; i++)
                if (!Gather(op.Arguments[i], invert ^ (op.Name == "/" && i == 1), powers, ref value)) return false;
            return true;
        }
    }
}
