using System.Text;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Images
{
    /// <summary>CSS Images compatibility -webkit-cross-fade(from-image, to-image, progress), as shipped by Chromium.</summary>
    public sealed class CssCrossFadeValue : CssValue
    {
        public CssCrossFadeValue(CssValue from,CssValue to,CssValue progress,bool? progressFromMath=null)
        { From=from; To=to; Progress=progress; ProgressFromMath=progressFromMath ?? progress is CssMathValue; }
        public CssValue From { get; }
        public CssValue To { get; }
        public CssValue Progress { get; }
        public bool ProgressFromMath { get; }
        public bool TryResolveProgress(CssMathEvaluationContext? context,out double progress)
        {
            progress=0;
            if (Progress is CssNumberValue number) progress=number.Value;
            else if (Progress is CssPercentageValue percentage) progress=percentage.Value/100;
            else if (Progress is CssMathValue math)
            {
                double basis=math.Type.PercentageHint==CssNumericDimension.Number ? 1 : 100;
                var evaluation=new CssMathEvaluationContext(context?.ResolveUnit,basis,math.Type.PercentageHint,context?.DevicePixelRatio,context?.ResolveNumericValue);
                if (!math.TryEvaluate(evaluation,out progress)) return false;
                if (math.Type.PercentageHint!=CssNumericDimension.Number && math.Fits("percentage")) progress/=100;
            }
            else return false;
            progress=System.Math.Clamp(double.IsNaN(progress) ? 0 : progress,0,1);
            return true;
        }
        public override void AppendTo(StringBuilder sb)
        {
            sb.Append("-webkit-cross-fade("); From.AppendTo(sb); sb.Append(", "); To.AppendTo(sb);
            sb.Append(", "); Progress.AppendTo(sb); sb.Append(')');
        }
    }
}
