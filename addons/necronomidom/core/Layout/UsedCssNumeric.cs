using System;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Layout;

/// <summary>Layout's final percentage basis over the existing computed numeric AST, never a string parser.</summary>
internal static class UsedCssNumeric
{
    internal static double Length(CssValue value, double basis)
    {
        double result = value switch
        {
            CssDimensionValue { Unit: "px" } px => px.Value,
            CssNumberValue { Value: 0 } => 0,
            CssPercentageValue percentage => percentage.Value * basis / 100,
            CssMathValue math when math.TryEvaluate(new CssMathEvaluationContext(percentageBasis: basis,
                percentageHint: CssNumericDimension.Length), out double evaluated) => evaluated,
            _ => throw new NotSupportedException("Runtime requires computed CSS length-percentage: " + value.Serialize())
        };
        if (!double.IsFinite(result)) throw new NotSupportedException("Nonfinite used CSS length.");
        return result;
    }
}
