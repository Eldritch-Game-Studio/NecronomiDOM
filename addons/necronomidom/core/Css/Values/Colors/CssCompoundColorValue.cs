using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Colors
{
    /// <summary>CSS Color 5 § 3 light-dark() selection in the used color scheme.</summary>
    public sealed class CssLightDarkColorValue : CssColorValue
    {
        public CssLightDarkColorValue(CssColorValue light,CssColorValue dark) { Light = light; Dark = dark; }
        public CssColorValue Light { get; }
        public CssColorValue Dark { get; }
        public override bool RequiresCurrentColor => Light.RequiresCurrentColor || Dark.RequiresCurrentColor;
        public override bool TryResolve(CssColorContext context,out CssResolvedColor color) => (context.DarkScheme ? Dark : Light).TryResolve(context,out color);
        public override void AppendTo(StringBuilder sb) { sb.Append("light-dark("); Light.AppendTo(sb); sb.Append(", "); Dark.AppendTo(sb); sb.Append(')'); }
    }

    /// <summary>CSS Color 5 color-mix() argument and its specified (not normalized) percentage.</summary>
    public sealed record CssColorMixItem(CssColorValue Color,CssValue? Percentage);

    /// <summary>CSS Color 5 § 3 color-mix(), with Oklab default and premultiplied, missing-aware interpolation.</summary>
    public sealed class CssMixColorValue : CssColorValue
    {
        public CssMixColorValue(string space,string hue,IReadOnlyList<CssColorMixItem> items)
        { Space = space; HueMethod = hue; Items = items; }
        public string Space { get; }
        public string HueMethod { get; }
        public IReadOnlyList<CssColorMixItem> Items { get; }
        public override bool RequiresCurrentColor
        {
            get { foreach (CssColorMixItem item in Items) if (item.Color.RequiresCurrentColor) return true; return false; }
        }
        internal static bool Percent(CssValue? value,out double result,CssMathEvaluationContext? numeric=null)
        {
            result = 0;
            if (value is CssPercentageValue p) { result=p.Value; return true; }
            return value is CssMathValue math && math.TryEvaluate(new CssMathEvaluationContext(numeric?.ResolveUnit,100,numeric?.PercentageHint,numeric?.DevicePixelRatio,numeric?.ResolveNumericValue),out result);
        }
        private bool Weights(Span<double> weights,CssColorContext context,out double sum)
        {
            sum=0; int omitted=0;
            for (int i=0;i<Items.Count;i++)
            {
                if (Items[i].Percentage==null) { omitted++; continue; }
                if (!Percent(Items[i].Percentage,out double percentage,context.NumericContext)) return false;
                weights[i]=System.Math.Clamp(double.IsNaN(percentage) ? 0 : percentage,0,100); sum+=weights[i];
            }
            double remainder = omitted==0 ? 0 : System.Math.Max(100-sum,0)/omitted;
            for (int i=0;i<Items.Count;i++) if (Items[i].Percentage==null) { weights[i]=remainder; sum+=remainder; }
            return true;
        }
        public override bool TryResolve(CssColorContext context,out CssResolvedColor color)
        {
            color = default;
            Span<double> weights = Items.Count<=16 ? stackalloc double[Items.Count] : new double[Items.Count];
            if (!Weights(weights,context,out double sum) || !Items[0].Color.TryResolve(context,out color)) return false;
            double accumulated=weights[0];
            if (Items.Count==1) color=CssColorInterpolation.ConvertMissing(color,Space);
            for (int i=1;i<Items.Count;i++)
            {
                if (!Items[i].Color.TryResolve(context,out CssResolvedColor next)) return false;
                double combined=accumulated+weights[i];
                color=CssColorInterpolation.Interpolate(color,next,Space,HueMethod,combined==0 ? 0.5 : weights[i]/combined);
                accumulated=combined;
            }
            color = color with { Alpha = color.Alpha*System.Math.Min(sum/100,1) };
            if (Space is "hsl" or "hwb" && color.Missing==0) color=color.ToSrgb();
            return true;
        }
        public override void AppendTo(StringBuilder sb)
        {
            sb.Append("color-mix(");
            if (Space!="oklab")
            {
                sb.Append("in ").Append(Space);
                if (HueMethod!="shorter") sb.Append(' ').Append(HueMethod).Append(" hue");
                sb.Append(", ");
            }
            Span<double> percentages = Items.Count<=16 ? stackalloc double[Items.Count] : new double[Items.Count];
            bool calculational=false; double specified=0; int omitted=0;
            foreach (CssColorMixItem item in Items)
            {
                if (item.Percentage is CssMathValue) calculational=true;
                if (item.Percentage==null) omitted++; else if (Percent(item.Percentage,out double p)) specified+=p;
            }
            bool equal=!calculational;
            double first=0;
            for (int i=0;i<Items.Count;i++)
            {
                percentages[i]=Items[i].Percentage==null ? System.Math.Max(100-specified,0)/omitted : Percent(Items[i].Percentage,out double p) ? p : 0;
                if (i==0) first=percentages[i];
                if (percentages[i]!=first || percentages[i]!=100d/Items.Count) equal=false;
            }
            for (int i=0;i<Items.Count;i++)
            {
                if (i>0) sb.Append(", "); Items[i].Color.AppendTo(sb);
                if (!equal)
                {
                    if (Items[i].Percentage!=null) { sb.Append(' '); Items[i].Percentage!.AppendTo(sb); }
                    else if (!calculational) sb.Append(' ').Append(NumberFormat.Format(percentages[i])).Append('%');
                }
            }
            sb.Append(')');
        }
    }
}
