using System;
using System.Globalization;
using System.Text;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Colors
{
    /// <summary>CSS Color 4 resolved coordinates. NaN coordinates preserve missing components during interpolation.</summary>
    public readonly record struct CssResolvedColor(string Space, double C1, double C2, double C3, double Alpha = 1)
    {
        public int Missing => (double.IsNaN(C1) ? 1 : 0) | (double.IsNaN(C2) ? 2 : 0) | (double.IsNaN(C3) ? 4 : 0) | (double.IsNaN(Alpha) ? 8 : 0);
        public CssResolvedColor ConvertTo(string space) => CssColorConversion.Convert(this, space);
        public CssResolvedColor ToSrgb() => ConvertTo("srgb");
        public string SerializeComputed(bool legacy=false)
        {
            var sb = new StringBuilder();
            AppendComputed(sb,legacy);
            return sb.ToString();
        }
        public void AppendComputed(StringBuilder sb,bool legacy=false) => CssColorSerialization.Append(sb,this,legacy);
    }

    /// <summary>CSS Color 4/5 context for currentcolor, system colors and light-dark().</summary>
    public sealed class CssColorContext
    {
        public CssResolvedColor CurrentColor { get; init; } = new("srgb", 0, 0, 0);
        public bool DarkScheme { get; init; }
        public Func<string, CssResolvedColor?>? SystemColor { get; init; }
        public CssMathEvaluationContext? NumericContext { get; init; }
    }

    /// <summary>Validated CSS Color 4/5 AST with explicit computed-color resolution.</summary>
    public abstract class CssColorValue : CssValue
    {
        public abstract bool RequiresCurrentColor { get; }
        public virtual bool IsLegacyRgb => false;
        public abstract bool TryResolve(CssColorContext context, out CssResolvedColor color);
    }

    /// <summary>An absolute Color 4 value or a context-dependent color keyword.</summary>
    public sealed class CssAbsoluteColorValue : CssColorValue
    {
        public CssAbsoluteColorValue(CssResolvedColor color, string? keyword = null, bool legacy = false)
        { Color = color; Keyword = keyword; Legacy = legacy; }
        public CssResolvedColor Color { get; }
        public string? Keyword { get; }
        public bool Legacy { get; }
        public override bool IsLegacyRgb => Legacy || Keyword!=null;
        internal CssRelativeColorValue? SpecifiedFunction { get; init; }
        public override bool RequiresCurrentColor => Keyword == "currentcolor";
        public override bool TryResolve(CssColorContext context, out CssResolvedColor color)
        {
            color = Color;
            if (Keyword == "currentcolor") color = context.CurrentColor;
            else if (Keyword != null && CssColorNames.IsSystem(Keyword))
            {
                CssResolvedColor? system = context.SystemColor?.Invoke(Keyword);
                if (!system.HasValue) return false;
                color = system.Value;
            }
            return true;
        }
        public override void AppendTo(StringBuilder sb)
        {
            if (Keyword != null) sb.Append(Keyword);
            else if (!Legacy && SpecifiedFunction is { ContainsMath:true }) SpecifiedFunction.AppendTo(sb);
            else CssColorSerialization.Append(sb, Color, Legacy);
        }
    }

    internal static class CssColorSerialization
    {
        internal static void Append(StringBuilder sb, CssResolvedColor color, bool legacy)
        {
            if (color.Space is "hsl" or "hwb" && (!legacy || color.Missing!=0))
            {
                sb.Append(color.Space).Append('('); HueChannel(sb,color.C1); sb.Append(' ');
                Channel(sb,color.C2*100); if (!double.IsNaN(color.C2)) sb.Append('%'); sb.Append(' ');
                Channel(sb,color.C3*100); if (!double.IsNaN(color.C3)) sb.Append('%');
                if (color.Alpha != 1) { sb.Append(" / "); Channel(sb,color.Alpha); }
                sb.Append(')'); return;
            }
            if (legacy && color.Missing!=0) legacy=false;
            if (legacy)
            {
                CssResolvedColor rgb = color.ToSrgb();
                double alpha = LegacyAlpha(System.Math.Clamp(double.IsNaN(rgb.Alpha) ? 0 : rgb.Alpha, 0, 1));
                sb.Append(alpha == 1 ? "rgb(" : "rgba(");
                LegacyChannel(sb,rgb.C1); sb.Append(", ");
                LegacyChannel(sb,rgb.C2); sb.Append(", ");
                LegacyChannel(sb,rgb.C3);
                if (alpha != 1) { sb.Append(", "); Channel(sb,alpha); }
                sb.Append(')'); return;
            }
            bool function = color.Space is "lab" or "lch" or "oklab" or "oklch";
            sb.Append(function ? color.Space : "color").Append('(');
            if (!function) sb.Append(color.Space).Append(' ');
            Channel(sb, color.C1); sb.Append(' '); Channel(sb, color.C2); sb.Append(' ');
            if (color.Space is "lch" or "oklch") HueChannel(sb,color.C3); else Channel(sb,color.C3);
            if (color.Alpha != 1) { sb.Append(" / "); Channel(sb, color.Alpha); }
            sb.Append(')');
        }
        private static void HueChannel(StringBuilder sb,double value)
        {
            // Rounding an angle just below a turn to 360 changes its parsed value to zero.
            // Retain enough precision to preserve that side of the discontinuity on round-trip.
            if (!double.IsFinite(value)) { Channel(sb,value); return; }
            string formatted=NumberFormat.Format(value);
            sb.Append(value>0 && value<360 && formatted=="360"
                ? value.ToString("G17",CultureInfo.InvariantCulture) : formatted);
        }
        internal static void Channel(StringBuilder sb, double value)
        {
            if (double.IsInfinity(value)) sb.Append("calc(").Append(NumberFormat.Format(value)).Append(')');
            else sb.Append(double.IsNaN(value) ? "none" : NumberFormat.Format(value));
        }
        private static void LegacyChannel(StringBuilder sb,double value)
        {
            double channel=Clean(System.Math.Clamp(value,0,1)*255);
            Channel(sb,System.Math.Floor(channel+0.5));
        }
        // CSSOM serialize an alpha value: choose the shortest two/three decimal representation of its byte.
        private static double LegacyAlpha(double alpha)
        {
            double encoded=System.Math.Clamp(System.Math.Floor((alpha+0.0000001)*255+0.5),0,255);
            double two=System.Math.Floor(encoded*100/255+0.5)/100;
            return System.Math.Floor(two*255+0.5)==encoded ? two : System.Math.Floor(encoded*1000/255+0.5)/1000;
        }
        private static double Clean(double value)
        {
            // Stabilize byte-quantization ties disturbed only by floating-point arithmetic.
            double half=System.Math.Round(value*2)/2;
            double roundoff=8*2.2204460492503131e-16*System.Math.Max(1,System.Math.Abs(value));
            return System.Math.Abs(value-half)<=roundoff ? half : value;
        }
    }
}
