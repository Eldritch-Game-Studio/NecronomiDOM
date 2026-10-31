using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Images
{
    /// <summary>CSS Images 3/4 URL, gradient and image-set grammar validation.</summary>
    public static partial class CssImageParser
    {
        public static bool IsImageFunction(string name) => CssColorTokens.IsAsciiName(name) && name.ToLowerInvariant() is
            "url" or "image" or "light-dark" or "image-set" or "-webkit-image-set" or "-webkit-cross-fade" or "linear-gradient" or "radial-gradient" or "conic-gradient" or
            "repeating-linear-gradient" or "repeating-radial-gradient" or "repeating-conic-gradient";
        public static bool TryParse(ComponentValue input,out CssValue? value)
        {
            value = null;
            if (input is PreservedToken { Token.Kind:CssTokenKind.Url } ||
                input is FunctionValue url && url.Name.Equals("url",StringComparison.OrdinalIgnoreCase))
                return CssTypeParser.TryParse("url",new[] { input },out value);
            if (input is not FunctionValue f || !IsImageFunction(f.Name)) return false;
            string name = f.Name.ToLowerInvariant();
            if (name is "image-set" or "-webkit-image-set") return TryImageSet(f,out value);
            if (name=="image") return TrySolidImage(f,out value);
            if (name=="light-dark") return TryLightDarkImage(f,out value);
            if (name=="-webkit-cross-fade") return TryLegacyCrossFade(f,out value);
            if (name.StartsWith("repeating-",StringComparison.Ordinal)) name = name[10..];
            if (name is not ("linear-gradient" or "radial-gradient" or "conic-gradient")) return false;
            return TryGradient(f,name,out value);
        }
        private static bool TryImageSet(FunctionValue f,out CssValue? value)
        {
            value = null;
            List<List<ComponentValue>> groups = CssColorParser.SplitCommas(f.Arguments);
            var options = new List<CssImageSetOption>(groups.Count);
            foreach (List<ComponentValue> group in groups)
            {
                if (group.Count is <1 or >3) return false;
                CssValue? image;
                if (group[0] is PreservedToken { Token.Kind: CssTokenKind.String } str) image = new CssUrlValue(str.Token.Value);
                else if (!TryParse(group[0],out image) || image is CssImageSetValue) return false;
                CssValue? resolution = null; string? mime = null;
                for (int i=1;i<group.Count;i++)
                {
                    if (group[i] is FunctionValue type && type.Name.Equals("type",StringComparison.OrdinalIgnoreCase))
                    {
                        List<ComponentValue> args = TokenText.WithoutWhitespace(type.Arguments);
                        if (mime!=null || args.Count!=1 || args[0] is not PreservedToken { Token.Kind: CssTokenKind.String } m) return false;
                        mime = m.Token.Value;
                    }
                    else
                    {
                        if (resolution!=null || !CssTypeParser.TryParse("resolution",new[] { group[i] },out resolution)) return false;
                        if (resolution is CssDimensionValue d && d.Value < 0) return false;
                    }
                }
                options.Add(new CssImageSetOption(image!,resolution,mime));
            }
            value = new CssImageSetValue(options); return true;
        }
        private static bool TryGradient(FunctionValue f,string kind,out CssValue? value)
        {
            value = null;
            List<List<ComponentValue>> groups = CssColorParser.SplitCommas(f.Arguments);
            if (groups.Count < 2) return false;
            List<ComponentValue>? preludeTokens=null;
            string space = "oklab", hue = "shorter";
            int start = 0;
            bool explicitInterpolation=false;
            if (!TryStop(groups[0],kind,out CssGradientStop? first) || first!.Color==null)
            {
                if (!TryPrelude(groups[0],kind,out preludeTokens,out space,out hue,out explicitInterpolation)) return false;
                start = 1;
            }
            var stops = new List<CssGradientStop>(groups.Count-start);
            int colors = 0;
            for (int i=start;i<groups.Count;i++)
            {
                CssGradientStop? stop=i==0 ? first : null;
                if (stop==null && !TryStop(groups[i],kind,out stop)) return false;
                if (stop!.Color == null && (i==start || i==groups.Count-1 || stops[^1].Color == null)) return false;
                if (stop.Color != null) colors++;
                stops.Add(stop);
            }
            if (colors < 2) return false;
            string defaultSpace=CssGradientValue.DefaultSpace(stops);
            if (!explicitInterpolation) space=defaultSpace;
            CssValue? prelude=preludeTokens==null ? null : CanonicalPrelude(preludeTokens,kind,space,hue,defaultSpace);
            value = new CssGradientValue(f.Name.ToLowerInvariant(),prelude,stops,space,hue); return true;
        }
        private static bool TryStop(List<ComponentValue> tokens,string kind,out CssGradientStop? stop)
        {
            stop = null;
            if (tokens.Count == 0) return false;
            CssColorValue? color = null;
            int start = 0;
            if (CssColorParser.TryParse(tokens[0],out CssValue? parsed) && parsed is CssColorValue c) { color = c; start = 1; }
            if (tokens.Count-start > (color == null ? 1 : 2) || (color == null && tokens.Count!=1)) return false;
            if (tokens.Count==start) { stop=new CssGradientStop(color,Array.Empty<CssValue>()); return true; }
            var positions = new List<CssValue>(tokens.Count-start);
            for (int i=start;i<tokens.Count;i++)
            {
                if (!TryCoordinate(tokens[i],kind == "conic-gradient" ? "angle-percentage" : "length-percentage",out CssValue? coordinate)) return false;
                positions.Add(coordinate!);
            }
            stop = new CssGradientStop(color,positions); return true;
        }
        internal static bool TryCoordinate(ComponentValue token,string type,out CssValue? value)
        {
            if (type != "angle-percentage") return CssTypeParser.TryParse(type,new[] { token },out value);
            if (token is FunctionValue f)
            {
                if (CssMathParser.TryParse(f,out value) && value is CssMathValue math && math.Fits(type)) return true;
                value=null; return false;
            }
            if (token is PreservedToken { Token.Kind:CssTokenKind.Number,Token.Number:0 }) { value=new CssDimensionValue(0,"deg"); return true; }
            return CssTypeParser.TryParse(token is PreservedToken { Token.Kind:CssTokenKind.Percentage } ? "percentage" : "angle",new[] { token },out value);
        }
    }
}
