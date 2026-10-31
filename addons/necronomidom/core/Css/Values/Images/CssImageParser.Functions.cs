using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Images
{
    public static partial class CssImageParser
    {
        private static readonly CssKeywordValue NoneImage=new("none");
        private static bool TrySolidImage(FunctionValue function,out CssValue? value)
        {
            value=null;
            if (!CssColorParser.TryParse(function.Arguments,out CssValue? parsed) || parsed is not CssColorValue color) return false;
            value=new CssSolidColorImageValue(color); return true;
        }
        private static bool TryLightDarkImage(FunctionValue function,out CssValue? value)
        {
            value=null;
            var groups=CssColorParser.SplitCommas(function.Arguments);
            if (groups.Count!=2 || groups[0].Count!=1 || groups[1].Count!=1 ||
                !TryImageOrNone(groups[0][0],out CssValue? light) || !TryImageOrNone(groups[1][0],out CssValue? dark)) return false;
            value=new CssLightDarkImageValue(light!,dark!); return true;
        }
        private static bool TryLegacyCrossFade(FunctionValue function,out CssValue? value)
        {
            value=null;
            var groups=CssColorParser.SplitCommas(function.Arguments);
            if (groups.Count!=3 || groups[0].Count!=1 || groups[1].Count!=1 || groups[2].Count!=1 ||
                !TryImageOrNone(groups[0][0],out CssValue? from) || !TryImageOrNone(groups[1][0],out CssValue? to)) return false;
            CssValue? progress;
            if (groups[2][0] is FunctionValue mathFunction)
            {
                if (!CssMathParser.TryParse(mathFunction,out progress) || progress is not CssMathValue math ||
                    !(math.Fits("number") || math.Fits("percentage"))) return false;
            }
            else if (groups[2][0] is PreservedToken p && p.Token.Kind is CssTokenKind.Number or CssTokenKind.Percentage)
                progress=new CssNumberValue(System.Math.Clamp(p.Token.Number/(p.Token.Kind==CssTokenKind.Percentage ? 100 : 1),0,1),false);
            else return false;
            value=new CssCrossFadeValue(from!,to!,progress!); return true;
        }
        private static bool TryImageOrNone(ComponentValue input,out CssValue? value)
        {
            if (CssColorParser.IsIdent(input,"none")) { value=NoneImage; return true; }
            return TryParse(input,out value);
        }
    }
}
