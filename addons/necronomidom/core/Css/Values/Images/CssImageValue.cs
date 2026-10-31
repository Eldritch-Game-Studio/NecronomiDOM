using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Images
{
    /// <summary>CSS Images 4 color stop or interpolation hint. A null color represents a hint.</summary>
    public sealed record CssGradientStop(CssColorValue? Color, IReadOnlyList<CssValue> Positions);

    /// <summary>CSS Images 3/4 validated linear, radial or conic gradient with typed colors and stop coordinates.</summary>
    public sealed class CssGradientValue : CssValue
    {
        public CssGradientValue(string name,CssValue? prelude,IReadOnlyList<CssGradientStop> stops,string? interpolationSpace = null,string hueMethod = "shorter")
        { Name = name; Prelude = prelude; Stops = stops; InterpolationSpace = interpolationSpace ?? DefaultSpace(stops); HueMethod = hueMethod; }
        public string Name { get; }
        public CssValue? Prelude { get; }
        public IReadOnlyList<CssGradientStop> Stops { get; }
        public string InterpolationSpace { get; }
        public string HueMethod { get; }
        internal static string DefaultSpace(IReadOnlyList<CssGradientStop> stops)
        {
            for (int i=0;i<stops.Count;i++) if (stops[i].Color is { IsLegacyRgb:false }) return "oklab";
            return "srgb";
        }
        public override void AppendTo(StringBuilder sb)
        {
            sb.Append(Name).Append('(');
            if (Prelude != null) { Prelude.AppendTo(sb); sb.Append(", "); }
            for (int i=0;i<Stops.Count;i++)
            {
                if (i>0) sb.Append(", ");
                CssGradientStop stop = Stops[i];
                stop.Color?.AppendTo(sb);
                for (int j=0;j<stop.Positions.Count;j++)
                {
                    if (j>0 || stop.Color!=null) sb.Append(' ');
                    stop.Positions[j].AppendTo(sb);
                }
            }
            sb.Append(')');
        }
    }

    /// <summary>CSS Images 4 image-set() option with image, resolution and optional MIME type.</summary>
    public sealed record CssImageSetOption(CssValue Image,CssValue? Resolution,string? MimeType);

    /// <summary>CSS Images 4 § 2.4 image-set() with individually validated candidates.</summary>
    public sealed class CssImageSetValue : CssValue
    {
        public CssImageSetValue(IReadOnlyList<CssImageSetOption> options) => Options = options;
        public IReadOnlyList<CssImageSetOption> Options { get; }
        public override void AppendTo(StringBuilder sb)
        {
            sb.Append("image-set(");
            for (int i=0;i<Options.Count;i++)
            {
                if (i>0) sb.Append(", ");
                CssImageSetOption option = Options[i]; option.Image.AppendTo(sb);
                if (option.Resolution!=null) { sb.Append(' '); option.Resolution.AppendTo(sb); }
                else sb.Append(" 1x");
                if (option.MimeType!=null) { sb.Append(" type("); CssSerialization.AppendString(sb,option.MimeType); sb.Append(')'); }
            }
            sb.Append(')');
        }
    }
}
