using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Colors
{
    public static partial class CssColorParser
    {
        private static bool TryLightDark(FunctionValue function,out CssValue? value)
        {
            value = null;
            List<List<ComponentValue>> parts = SplitCommas(function.Arguments);
            if (parts.Count != 2 || !TryParse(parts[0],out CssValue? light) || !TryParse(parts[1],out CssValue? dark) || light is not CssColorValue l || dark is not CssColorValue d) return false;
            value = new CssLightDarkColorValue(l,d); return true;
        }
        private static bool TryMix(FunctionValue function,out CssValue? value)
        {
            value = null;
            List<List<ComponentValue>> parts = SplitCommas(function.Arguments);
            string space = "oklab", hue = "shorter";
            int start = 0;
            if (parts[0].Count>0 && IsIdent(parts[0][0],"in"))
            {
                if (!TryInterpolation(parts[0],out space,out hue)) return false;
                start = 1;
            }
            if (start == parts.Count) return false;
            var items = new List<CssColorMixItem>();
            for (int i=start;i<parts.Count;i++)
            {
                if (!TryMixColor(parts[i],out CssColorValue? color,out CssValue? percentage)) return false;
                items.Add(new CssColorMixItem(color!,percentage));
            }
            value = new CssMixColorValue(space,hue,items); return true;
        }
        internal static bool TryInterpolation(IReadOnlyList<ComponentValue> tokens,out string space,out string hue)
        {
            space = "oklab"; hue = "shorter";
            if (tokens.Count is not (2 or 4) || !IsIdent(tokens[0],"in") || tokens[1] is not PreservedToken p || p.Token.Kind != CssTokenKind.Ident) return false;
            if (!CssColorTokens.IsAsciiName(p.Token.Value)) return false;
            space = p.Token.Value.ToLowerInvariant();
            if (!IsInterpolationSpace(space)) return false;
            if (space=="xyz") space="xyz-d65";
            if (tokens.Count == 4)
            {
                if (space is not ("hsl" or "hwb" or "lch" or "oklch") || !IsIdent(tokens[3],"hue") || tokens[2] is not PreservedToken h) return false;
                hue = h.Token.Value.ToLowerInvariant();
                if (hue is not ("shorter" or "longer" or "increasing" or "decreasing")) return false;
            }
            return true;
        }
        private static bool TryMixColor(List<ComponentValue> tokens,out CssColorValue? color,out CssValue? percentage)
        {
            color = null; percentage = null;
            if (tokens.Count is not (1 or 2)) return false;
            int colorIndex = 0;
            if (tokens.Count == 2)
            {
                int percentIndex = CssTypeParser.TryParse("percentage",new[] { tokens[0] },out _) ? 0 : 1;
                if (!CssTypeParser.TryParse("percentage",new[] { tokens[percentIndex] },out percentage)) return false;
                if (percentage is CssPercentageValue number && number.Value is <0 or >100) return false;
                colorIndex = 1-percentIndex;
            }
            if (!TryParse(tokens[colorIndex],out CssValue? parsed) || parsed is not CssColorValue c) return false;
            color = c; return true;
        }
    }
}
