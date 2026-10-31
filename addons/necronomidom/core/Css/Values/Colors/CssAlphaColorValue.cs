using System;
using System.Text;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Colors
{
    /// <summary>Color 5 alpha-only relative color, preserving the origin color space and channel precision.</summary>
    public sealed class CssAlphaColorValue : CssColorValue
    {
        internal CssAlphaColorValue(CssColorValue origin,CssValue alpha) { Origin=origin; Alpha=alpha; }
        public CssColorValue Origin { get; }
        public CssValue Alpha { get; }
        public override bool RequiresCurrentColor => Origin.RequiresCurrentColor;
        public override bool TryResolve(CssColorContext context,out CssResolvedColor color)
        {
            if (!Origin.TryResolve(context,out color)) return false;
            if (Origin.IsLegacyRgb) color=color.ToSrgb();
            if (Alpha is CssKeywordValue { Value:"alpha" }) return true;
            double alpha;
            if (Alpha is CssNumberValue number) alpha=number.Value;
            else if (Alpha is CssPercentageValue percentage) alpha=percentage.Value/100;
            else if (Alpha is CssKeywordValue { Value:"none" }) alpha=double.NaN;
            else if (Alpha is CssMathValue math)
            {
                double originAlpha=color.Alpha;
                double? Resolve(CssValue value) => value is CssKeywordValue { Value:"alpha" } ? originAlpha : context.NumericContext?.ResolveNumericValue?.Invoke(value);
                CssMathEvaluationContext? numeric=context.NumericContext;
                bool percentMath=math.Fits("percentage");
                var evaluation=new CssMathEvaluationContext(numeric?.ResolveUnit,100,math.Type.PercentageHint,numeric?.DevicePixelRatio,Resolve);
                if (!math.TryEvaluate(evaluation,out alpha)) return false;
                if (percentMath) alpha/=100;
                if (double.IsNaN(alpha)) alpha=0;
            }
            else return false;
            color=color with { Alpha=double.IsNaN(alpha) ? alpha : System.Math.Clamp(alpha,0,1) };
            return true;
        }
        public override void AppendTo(StringBuilder sb)
        {
            sb.Append("alpha(from "); Origin.AppendTo(sb);
            sb.Append(" / "); Alpha.AppendTo(sb);
            sb.Append(')');
        }
    }
}
