using System;
using System.Collections.Generic;
using M = System.Math;

namespace NecronomiDom.Css.Values.Math
{
    internal static partial class CssMathSimplifier
    {
        internal static CssMathExpression Simplify(CssMathExpression expression, CssMathEvaluationContext context, bool computed = false)
        {
            if (expression is CssMathLiteral literal)
            {
                if (literal.IsUnbounded) return literal;
                if (literal.Unit == "%" && !context.PercentageHint.HasValue) return literal;
                CssNumericType type = literal.Type;
                if (literal.Unit == "%" && context.PercentageHint.HasValue) type = type.Hint(context.PercentageHint.Value);
                if (computed && (double.IsNaN(literal.Value) || literal.Value == 0 && literal.Unit != "%") &&
                    CssMathUnits.TryGet(literal.Unit, out _, out _))
                    return new CssMathLiteral(literal.Value, type.CanonicalUnit, type);
                if (literal.Unit == "%")
                {
                    if (!context.TryEvaluatePercentage(literal.Value, out double percentage)) return literal;
                    return new CssMathLiteral(percentage, type.CanonicalUnit, type);
                }
                if (!context.TryScale(literal.Unit, out double scale)) return literal;
                return new CssMathLiteral(literal.Value * scale, type.CanonicalUnit, type);
            }
            if (expression is CssMathNumericLeaf leaf)
            {
                if (!leaf.TryEvaluate(context, out double leafValue)) return leaf;
                CssNumericType leafType = leaf.Type;
                if (context.PercentageHint.HasValue && leafType.Percentage != 0) leafType = leafType.Hint(context.PercentageHint.Value);
                return new CssMathLiteral(leafValue, leafType.CanonicalUnit, leafType);
            }
            var op = (CssMathOperation)expression;
            CssNumericType resultType = op.Type;
            if (context.PercentageHint.HasValue && resultType.Percentage != 0) resultType = resultType.Hint(context.PercentageHint.Value);
            var children = new List<CssMathExpression>(op.Arguments.Count);
            foreach (CssMathExpression child in op.Arguments) children.Add(Simplify(child, context, computed));
            string name = op.Name;
            if (name == "invert")
            {
                if (children[0] is CssMathLiteral { Unit: "" } scalar)
                    return new CssMathLiteral(1 / scalar.Value, "", resultType);
                children.Insert(0, new CssMathLiteral(1, "", default));
                name = "/";
            }
            if (name == "-")
            {
                children[1] = Negate(children[1]);
                return Sum(children, resultType, computed);
            }
            if (name == "+") return Sum(children, resultType, computed);
            if (name == "negate") return Negate(children[0]);
            if (name == "/" && children[1] is CssMathLiteral denominator && denominator.Unit == "")
            {
                children[1] = new CssMathLiteral(1 / denominator.Value, "", default);
                return Product(children, resultType, context, computed);
            }
            if (name == "*") return Product(children, resultType, context, computed);
            var updated = new CssMathOperation(name, children.ToArray(), resultType, op.Strategy);
            if (name == "/" && resultType.IsSimple && TryLiteralAlgebra(updated, out double ratio, out string ratioUnit))
                return new CssMathLiteral(ratio, ratioUnit, resultType);
            bool canEvaluate = resultType.IsSimple && CanEvaluate(children, context);
            if (resultType.IsSimple && (canEvaluate || computed) && updated.TryEvaluate(context, out double value) &&
                (canEvaluate || double.IsNaN(value)))
                return new CssMathLiteral(value, resultType.CanonicalUnit, resultType);
            if (name is "min" or "max")
            {
                MergeLiterals(children, name);
                if (children.Count == 1) return children[0];
                return new CssMathOperation(name, children.ToArray(), resultType);
            }
            return updated;
        }
        private static bool CanEvaluate(IReadOnlyList<CssMathExpression> children, CssMathEvaluationContext context)
        {
            foreach (CssMathExpression child in children)
            {
                if (child is CssMathOperation operation)
                {
                    if (!CanEvaluate(operation.Arguments, context)) return false;
                }
                else if (child is CssMathLiteral literal)
                {
                    if (literal.IsUnbounded) continue;
                    if (literal.Unit == "%")
                    {
                        if (!context.PercentageHint.HasValue ||
                            !context.PercentageBasis.HasValue && context.PercentageHint != CssNumericDimension.Number) return false;
                    }
                    else if (!CssMathUnits.TryGet(literal.Unit, out _, out double? scale) || !scale.HasValue) return false;
                }
                else return false;
            }
            return true;
        }
        private static CssMathExpression Negate(CssMathExpression child)
        {
            if (child is CssMathLiteral literal) return new CssMathLiteral(-literal.Value, literal.Unit, literal.Type);
            if (child is CssMathOperation { Name: "negate", Arguments.Count: 1 } negation) return negation.Arguments[0];
            if (child is CssMathOperation { Name: "*", Arguments.Count: 2 } product &&
                product.Arguments[0] is CssMathLiteral { Unit: "", Value: -1 })
                return product.Arguments[1];
            if (child is CssMathOperation { Name: "+" } sum)
            {
                var children = new CssMathExpression[sum.Arguments.Count];
                for (int i = 0; i < children.Length; i++) children[i] = Negate(sum.Arguments[i]);
                return new CssMathOperation("+", children, sum.Type);
            }
            return new CssMathOperation("*", new CssMathExpression[] { new CssMathLiteral(-1, "", default), child }, child.Type);
        }
        private static void Flatten(List<CssMathExpression> children, string name)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not CssMathOperation op || op.Name != name) continue;
                children.RemoveAt(i); children.InsertRange(i, op.Arguments); i--;
            }
        }
        private static CssMathExpression Sum(List<CssMathExpression> children, CssNumericType type, bool computed = false)
        {
            Flatten(children, "+"); MergeLiterals(children, "+");
            if (computed)
                foreach (CssMathExpression child in children)
                    if (child is CssMathLiteral literal && double.IsNaN(literal.Value))
                        return new CssMathLiteral(double.NaN, type.CanonicalUnit, type);
            if (computed && children.Count > 1)
                for (int i = children.Count - 1; i >= 0 && children.Count > 1; i--)
                    if (children[i] is CssMathLiteral { Unit: "px", Value: 0 }) children.RemoveAt(i);
            var literals = new List<CssMathExpression>();
            var others = new List<CssMathExpression>();
            foreach (CssMathExpression child in children)
                if (child is CssMathLiteral) literals.Add(child); else others.Add(child);
            literals.Sort(CompareTerms);
            children.Clear(); children.AddRange(literals); children.AddRange(others);
            return children.Count == 1 ? children[0] : new CssMathOperation("+", children.ToArray(), type);
        }
        private static int CompareTerms(CssMathExpression a, CssMathExpression b)
        {
            if (a is not CssMathLiteral al) return b is CssMathLiteral ? 1 : 0;
            if (b is not CssMathLiteral bl) return -1;
            if (al.Unit == bl.Unit) return 0;
            if (al.Unit == "") return -1;
            if (bl.Unit == "") return 1;
            if (al.Unit == "%") return -1;
            if (bl.Unit == "%") return 1;
            return string.CompareOrdinal(al.Unit, bl.Unit);
        }
        private static void MergeLiterals(List<CssMathExpression> children, string operation)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not CssMathLiteral first || first.IsUnbounded || operation != "+" && first.Unit == "%") continue;
                double value = first.Value;
                for (int j = i + 1; j < children.Count; j++)
                {
                    if (children[j] is not CssMathLiteral next || next.Unit != first.Unit || next.IsUnbounded) continue;
                    value = operation switch { "+" => value + next.Value, "min" => M.Min(value, next.Value), _ => M.Max(value, next.Value) };
                    children.RemoveAt(j--);
                }
                children[i] = new CssMathLiteral(value, first.Unit, first.Type);
            }
        }
    }
}
