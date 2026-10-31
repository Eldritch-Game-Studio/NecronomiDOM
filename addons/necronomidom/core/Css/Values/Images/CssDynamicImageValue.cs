using System.Text;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Css.Values.Images
{
    /// <summary>CSS Images 4 image(&lt;color&gt;): a solid-color image without natural dimensions.</summary>
    public sealed class CssSolidColorImageValue : CssValue
    {
        public CssSolidColorImageValue(CssColorValue color) { Color=color; }
        public CssColorValue Color { get; }
        public override void AppendTo(StringBuilder sb)
        { sb.Append("image("); Color.AppendTo(sb); sb.Append(')'); }
    }
    /// <summary>CSS Color 5 image-form light-dark(), selecting a branch in the element color scheme.</summary>
    public sealed class CssLightDarkImageValue : CssValue
    {
        private static readonly CssSolidColorImageValue TransparentImage=new(new CssAbsoluteColorValue(new("srgb",0,0,0,0),"transparent"));
        public CssLightDarkImageValue(CssValue light,CssValue dark) { Light=light; Dark=dark; }
        public CssValue Light { get; }
        public CssValue Dark { get; }
        public CssValue Select(bool darkScheme)
        {
            CssValue selected=darkScheme ? Dark : Light;
            return selected is CssKeywordValue { Value:"none" } ? TransparentImage : selected;
        }
        public override void AppendTo(StringBuilder sb)
        { sb.Append("light-dark("); Light.AppendTo(sb); sb.Append(", "); Dark.AppendTo(sb); sb.Append(')'); }
    }
}
