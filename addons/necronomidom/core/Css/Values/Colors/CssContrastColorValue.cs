using System.Text;

namespace NecronomiDom.Css.Values.Colors
{
    /// <summary>Color 5 UA-defined black/white selection using the pinned Chromium WCAG contrast-ratio algorithm.</summary>
    public sealed class CssContrastColorValue : CssColorValue
    {
        internal CssContrastColorValue(CssColorValue background) { Background=background; }
        public CssColorValue Background { get; }
        public override bool IsLegacyRgb => true;
        public override bool RequiresCurrentColor => Background.RequiresCurrentColor;
        public override bool TryResolve(CssColorContext context,out CssResolvedColor color)
        {
            color=default;
            if (!Background.TryResolve(context,out CssResolvedColor background)) return false;
            CssResolvedColor rgb=background.ToSrgb();
            double luminance=0.2126*Linear(rgb.C1)+0.7152*Linear(rgb.C2)+0.0722*Linear(rgb.C3);
            double black=(luminance+0.05)/0.05, white=1.05/(luminance+0.05);
            double channel=black>white ? 0 : 1;
            color=new("srgb",channel,channel,channel); return true;
        }
        private static double Linear(double value)
        {
            if (double.IsNaN(value)) value=0;
            double encoded=System.Math.Floor(System.Math.Clamp(value,0,1)*255+0.5)/255;
            return encoded<=0.04045 ? encoded/12.92 : System.Math.Pow((encoded+0.055)/1.055,2.4);
        }
        public override void AppendTo(StringBuilder sb)
        { sb.Append("contrast-color("); Background.AppendTo(sb); sb.Append(')'); }
    }
}
