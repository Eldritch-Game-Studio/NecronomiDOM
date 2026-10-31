using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Colors
{
    /// <summary>CSS Color 5 § 4 relative color channels, evaluated in the originating color's converted space.</summary>
    public sealed partial class CssRelativeColorValue : CssColorValue
    {
        private readonly IReadOnlyList<CssValue> serializedChannels;
        internal CssRelativeColorValue(string space,CssColorValue? origin,IReadOnlyList<ComponentValue> channels)
        {
            Space=space; Origin=origin; Channels=channels;
            IsLegacyRgb=origin==null && space is "rgb" or "hsl" or "hwb";
            for (int i=0;i<channels.Count;i++)
            {
                if (channels[i] is FunctionValue) ContainsMath=true;
                if (CssColorParser.IsIdent(channels[i],"none")) IsLegacyRgb=false;
            }
            serializedChannels=SerializeChannels();
        }
        public string Space { get; }
        public CssColorValue? Origin { get; }
        public IReadOnlyList<ComponentValue> Channels { get; }
        public bool UsesRelativeSyntax => Origin!=null;
        public override bool IsLegacyRgb { get; }
        internal bool ContainsMath { get; }
        public override bool RequiresCurrentColor => Origin?.RequiresCurrentColor ?? false;
        public override bool TryResolve(CssColorContext context,out CssResolvedColor color)
        {
            color = default;
            IReadOnlyDictionary<string,double>? symbols = null;
            double alpha = 1;
            if (Origin != null)
            {
                if (!Origin.TryResolve(context,out CssResolvedColor origin)) return false;
                origin = CssColorInterpolation.ConvertMissing(origin,Space == "rgb" ? "srgb" : Space);
                symbols = CssColorChannels.Symbols(origin,Space); alpha = origin.Alpha;
            }
            if (!TryChannel(0,symbols,context,out double c1) || !TryChannel(1,symbols,context,out double c2) || !TryChannel(2,symbols,context,out double c3)) return false;
            if (Channels.Count == 4 && !TryChannel(3,symbols,context,out alpha)) return false;
            color = CssColorChannels.Clamp(Space,c1,c2,c3,alpha);
            if (Origin != null && Space == "rgb")
                color = new("srgb",c1,c2,c3,color.Alpha);
            if (Origin != null && Space is "hsl" or "hwb" && color.Missing==0) color = color.ToSrgb();
            return true;
        }
        public override void AppendTo(StringBuilder sb)
        {
            bool predefined = CssColorParser.IsSpace(Space);
            sb.Append(predefined ? "color" : Space).Append('(');
            if (Origin != null)
            {
                sb.Append("from ");
                Origin.AppendTo(sb);
                sb.Append(' ');
            }
            if (predefined) sb.Append(Space).Append(' ');
            for (int i = 0; i < Channels.Count; i++)
            {
                if (i == 3 && Origin == null && serializedChannels[i] is CssNumberValue { Value: 1 }) continue;
                if (i > 0) sb.Append(i == 3 ? " / " : " ");
                serializedChannels[i].AppendTo(sb);
            }
            sb.Append(')');
        }
    }
}
