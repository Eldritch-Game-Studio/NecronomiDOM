using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Colors
{
    public static partial class CssColorParser
    {
        private static readonly string[] AlphaNames={"alpha"};
        private static bool TryContrast(FunctionValue function,out CssValue? value)
        {
            value=null;
            List<ComponentValue> tokens=TokenText.WithoutWhitespace(function.Arguments);
            if (tokens.Count!=1 || !TryParse(tokens[0],out CssValue? parsed) || parsed is not CssColorValue color) return false;
            value=new CssContrastColorValue(color); return true;
        }
        private static bool TryAlpha(FunctionValue function,out CssValue? value)
        {
            value=null;
            List<ComponentValue> tokens=TokenText.WithoutWhitespace(function.Arguments);
            if (tokens.Count!=4 || !IsIdent(tokens[0],"from") ||
                !TryParse(tokens[1],out CssValue? parsed) || parsed is not CssColorValue origin ||
                !IsSlash(tokens[2]) || !TryAlphaChannel(tokens[3],out CssValue? alpha)) return false;
            value=new CssAlphaColorValue(origin,alpha!); return true;
        }
        private static bool TryAlphaChannel(ComponentValue token,out CssValue? value)
        {
            value=null;
            if (token is FunctionValue function)
                return CssMathParser.TryParseChannels(function,AlphaNames,out value) && value is CssMathValue math && (math.Fits("number") || math.Fits("percentage"));
            if (token is not PreservedToken p) return false;
            if (p.Token.Kind==CssTokenKind.Number) value=new CssNumberValue(p.Token.Number,false);
            else if (p.Token.Kind==CssTokenKind.Percentage) value=new CssPercentageValue(p.Token.Number);
            else if (IsIdent(token,"alpha")) value=new CssKeywordValue("alpha");
            else if (IsIdent(token,"none")) value=new CssKeywordValue("none");
            return value!=null;
        }
    }
}
