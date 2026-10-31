using System;
using System.Text;

namespace NecronomiDom.Css.Values.Math
{
    /// <summary>CSS Values 5 § 11 intrinsic sizing calculation; preserves the basis's layout behavior.</summary>
    public sealed class CssSizingCalculationValue : CssValue
    {
        public CssSizingCalculationValue(CssValue basis, CssMathValue calculation)
        { Basis = basis; Calculation = calculation; UsesSize = ContainsSize(calculation.Root); }
        public CssValue Basis { get; }
        public CssMathValue Calculation { get; }
        public bool UsesSize { get; }
        public bool FitsProperty(string property)
        {
            if (Basis is CssSizingCalculationValue nested) return nested.FitsProperty(property);
            if (Basis is not CssKeywordValue keyword) return true;
            return keyword.Value switch
            {
                "auto" => !property.StartsWith("max-", StringComparison.Ordinal),
                "content" => property == "flex-basis", _ => true
            };
        }
        public CssSizingCalculationValue Simplify(CssMathEvaluationContext context)
        {
            CssValue basis = Basis switch
            {
                CssMathValue numeric => numeric.Simplify(context),
                CssDimensionValue dimension => NormalizeDimension(dimension, context),
                CssPercentageValue percentage when context.PercentageBasis.HasValue =>
                    new CssDimensionValue(percentage.Value * context.PercentageBasis.Value / 100, "px"),
                CssSizingCalculationValue nested => nested.Simplify(context), _ => Basis
            };
            var specifiedSize = new CssMathEvaluationContext(context.ResolveUnit, context.PercentageBasis,
                CssNumericDimension.Length, context.DevicePixelRatio, context.ResolveNumericValue);
            return new CssSizingCalculationValue(basis, Calculation.Simplify(specifiedSize));
        }
        /// <summary>Resolve at used-value time; indefinite percentages in the calculation (not basis) become zero.</summary>
        public bool TryEvaluate(CssMathEvaluationContext context, out double value) => Evaluate(context, true, out value);
        /// <summary>Evaluate only layout-independent numeric bases; missing percentage bases never become zero here.</summary>
        public bool TryCompute(CssMathEvaluationContext context, out double value) => Evaluate(context, false, out value);
        private bool Evaluate(CssMathEvaluationContext context, bool used, out double value)
        {
            value = 0;
            double? size = null;
            if (UsesSize || !used)
            {
                if (Basis is CssMathValue or CssDimensionValue or CssPercentageValue)
                {
                    if (!TryNumericBasis(context, out double basisValue)) return false;
                    size = basisValue;
                }
                else if (Basis is CssSizingCalculationValue nested)
                {
                    double basisValue;
                    bool resolved = used ? nested.TryEvaluate(context, out basisValue) : nested.TryCompute(context, out basisValue);
                    if (!resolved) return false;
                    size = basisValue;
                }
                else if (Basis is not CssKeywordValue { Value: "any" })
                {
                    if (!used) return false;
                    size = context.ResolveNumericValue?.Invoke(Basis);
                    if (!size.HasValue) return false;
                }
            }
            if (!used && Basis is not (CssMathValue or CssDimensionValue or CssPercentageValue or CssSizingCalculationValue) &&
                Basis is not CssKeywordValue { Value: "any" }) return false;
            var evaluation = new CssMathEvaluationContext(context.ResolveUnit,
                context.PercentageBasis ?? (used ? 0.0 : null), CssNumericDimension.Length,
                context.DevicePixelRatio, context.ResolveNumericValue, size);
            return Calculation.TryEvaluate(evaluation, out value);
        }
        private static CssValue NormalizeDimension(CssDimensionValue dimension, CssMathEvaluationContext context)
        {
            if (!CssMathUnits.TryGet(dimension.Unit, out CssNumericDimension type, out _) ||
                !context.TryScale(dimension.Unit, out double scale)) return dimension;
            string canonical = CssMathUnits.Canonical(type);
            return scale == 1 && dimension.Unit == canonical ? dimension :
                new CssDimensionValue(dimension.Value * scale, canonical);
        }
        private bool TryNumericBasis(CssMathEvaluationContext context, out double value)
        {
            value = 0;
            if (Basis is CssMathValue math) return math.TryEvaluate(context, out value);
            if (Basis is CssDimensionValue dimension && context.TryScale(dimension.Unit, out double scale))
            {
                value = dimension.Value * scale;
                return true;
            }
            if (Basis is CssPercentageValue percentage && context.PercentageBasis.HasValue)
            {
                value = percentage.Value * context.PercentageBasis.Value / 100;
                return true;
            }
            return false;
        }
        public override void AppendTo(StringBuilder builder)
        {
            builder.Append("calc-size(");
            if (Basis is CssMathValue numeric) numeric.Root.AppendTo(builder, 0); else Basis.AppendTo(builder);
            builder.Append(", "); Calculation.Root.AppendTo(builder, 0); builder.Append(')');
        }
        private static bool ContainsSize(CssMathExpression expression)
        {
            if (expression is CssMathNumericLeaf { Value: CssKeywordValue { Value: "size" } }) return true;
            if (expression is CssMathOperation operation)
                foreach (CssMathExpression child in operation.Arguments) if (ContainsSize(child)) return true;
            return false;
        }
    }
}
