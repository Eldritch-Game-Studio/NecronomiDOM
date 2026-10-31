using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values
{
    // Typed OM reifies object/perspective positions as offsets, not CSSOM's edge keywords.
    // This is a projection of the grammar's existing position value, not another parser/model.
    internal static class CssPositionTypedValues
    {
        internal static CssValue Reify(CssValue value)
        {
            if (value is CssWideKeyword or CssPendingSubstitutionValue) return value;
            if (value is not CssListValue { Separator: CssListSeparator.Space } position) return value;
            bool needsProjection = false;
            foreach (CssValue item in position.Items)
                if (item is CssKeywordValue) { needsProjection = true; break; }
            if (!needsProjection) return value;
            var result = new List<CssValue>(2);
            for (int i = 0; i < position.Items.Count; i++)
            {
                CssValue item = position.Items[i];
                if (item is CssKeywordValue edge && edge.Value is "left" or "right" or "top" or "bottom"
                    && i + 1 < position.Items.Count && position.Items[i + 1] is not CssKeywordValue)
                {
                    CssValue offset = position.Items[++i];
                    result.Add(edge.Value is "right" or "bottom" ? Complement(offset) : offset);
                }
                else result.Add(item is CssKeywordValue keyword ? keyword.Value switch
                {
                    "left" or "top" => new CssPercentageValue(0),
                    "right" or "bottom" => new CssPercentageValue(100),
                    "center" => new CssPercentageValue(50),
                    _ => item
                } : item);
            }
            return new CssListValue(result, CssListSeparator.Space);
        }

        private static CssValue Complement(CssValue offset)
        {
            var percentage = CssNumericType.For(CssNumericDimension.Percentage);
            var length = CssNumericType.For(CssNumericDimension.Length);
            var type = length with { PercentageHint = CssNumericDimension.Length };
            CssMathExpression root = offset switch
            {
                CssPercentageValue percent => new CssMathLiteral(percent.Value, "%", percentage),
                CssDimensionValue dimension => new CssMathLiteral(dimension.Value, dimension.Unit, length),
                CssNumberValue number => new CssMathLiteral(number.Value, "px", length),
                CssMathValue math => math.Root,
                _ => throw new System.InvalidOperationException("Position offset was not a length-percentage.")
            };
            var terms = new List<CssMathExpression> { new CssMathLiteral(100, "%", percentage) };
            NegateTerms(root, terms);
            return new CssMathValue(new CssMathOperation("+", terms, type), type, true);
        }

        private static void NegateTerms(CssMathExpression expression, List<CssMathExpression> terms)
        {
            if (expression is CssMathOperation { Name: "+" } sum)
                foreach (CssMathExpression term in sum.Arguments) NegateTerms(term, terms);
            else if (expression is CssMathLiteral literal)
                terms.Add(new CssMathLiteral(-literal.Value, literal.Unit, literal.Type));
            else terms.Add(new CssMathOperation("negate", new[] { expression }, expression.Type));
        }
    }
}
