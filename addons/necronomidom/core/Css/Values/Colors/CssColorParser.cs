using System;
using System.Collections.Generic;
using System.Globalization;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Colors
{
    /// <summary>CSS Color 4 § 4–12 and Color 5 functional-color validation.</summary>
    public static partial class CssColorParser
    {
        public static bool IsColorFunction(string name) => CssColorTokens.IsAsciiName(name) && name.ToLowerInvariant() is
            "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color" or "color-mix" or "light-dark" or "alpha" or "contrast-color";
        public static bool TryParse(IReadOnlyList<ComponentValue> input, out CssValue? value)
        {
            value=null; ComponentValue? single=null;
            for (int i=0;i<input.Count;i++)
            {
                if (TokenText.IsWhitespace(input[i])) continue;
                if (single!=null) return false;
                single=input[i];
            }
            return single!=null && TryParse(single,out value);
        }
        public static bool TryParse(ComponentValue input, out CssValue? value)
        {
            value = null;
            if (input is PreservedToken t)
            {
                if (!CssColorTokens.IsAsciiName(t.Token.Value)) return false;
                string name = t.Token.Value.ToLowerInvariant();
                if (t.Token.Kind == CssTokenKind.Ident)
                {
                    if (CssColorNames.TryGet(name,out CssResolvedColor named)) value = new CssAbsoluteColorValue(named,name);
                    else if (name == "transparent") value = new CssAbsoluteColorValue(new("srgb",0,0,0,0),name);
                    else if (name == "currentcolor" || CssColorNames.IsSystem(name)) value = new CssAbsoluteColorValue(default,name);
                }
                else if (t.Token.Kind == CssTokenKind.Hash && TryHex(name,out CssResolvedColor hex)) value = new CssAbsoluteColorValue(hex,null,true);
                return value != null;
            }
            if (input is not FunctionValue f || !CssColorTokens.IsAsciiName(f.Name)) return false;
            string function = f.Name.ToLowerInvariant();
            if (function == "light-dark") return TryLightDark(f,out value);
            if (function == "color-mix") return TryMix(f,out value);
            if (function == "alpha") return TryAlpha(f,out value);
            if (function == "contrast-color") return TryContrast(f,out value);
            if (function is not ("rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color")) return false;
            return TryFunctional(f,out value);
        }
        private static bool TryHex(string text,out CssResolvedColor color)
        {
            color = default;
            if (text.Length is not (3 or 4 or 6 or 8) || !uint.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint n)) return false;
            double r,g,b,a = 1;
            if (text.Length <= 4)
            {
                if (text.Length == 4) { a = (n & 15)/15d; n >>= 4; }
                r = (n >> 8 & 15)/15d; g = (n >> 4 & 15)/15d; b = (n & 15)/15d;
            }
            else
            {
                if (text.Length == 8) { a = (n & 255)/255d; n >>= 8; }
                r = (n >> 16 & 255)/255d; g = (n >> 8 & 255)/255d; b = (n & 255)/255d;
            }
            color = new("srgb",r,g,b,a); return true;
        }
        internal static bool IsIdent(ComponentValue token,string name) => token is PreservedToken p && p.Token.IsIdent(name);
        internal static bool IsSlash(ComponentValue token) => token is PreservedToken p && p.Token.IsDelim('/');
        internal static List<List<ComponentValue>> SplitCommas(IReadOnlyList<ComponentValue> input)
        {
            var result = new List<List<ComponentValue>> { new() };
            foreach (ComponentValue item in input)
            {
                if (TokenText.IsWhitespace(item)) continue;
                if (item is PreservedToken p && p.Token.Kind == CssTokenKind.Comma) result.Add(new());
                else result[^1].Add(item);
            }
            return result;
        }
        internal static bool IsSpace(string name) => name is "srgb" or "srgb-linear" or "display-p3" or "display-p3-linear" or "a98-rgb" or "prophoto-rgb" or "rec2020" or "xyz" or "xyz-d50" or "xyz-d65";
        internal static bool IsInterpolationSpace(string name) => IsSpace(name) || name is "lab" or "lch" or "oklab" or "oklch" or "hsl" or "hwb";
    }
}
