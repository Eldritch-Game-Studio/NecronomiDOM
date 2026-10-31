using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Images
{
    public static partial class CssImageParser
    {
        private static bool TryPrelude(List<ComponentValue> input,string kind,out List<ComponentValue>? value,out string space,out string hue,out bool interpolation)
        {
            value=null; space="oklab"; hue="shorter"; interpolation=false;
            if (input.Count == 0) return false;
            var rest = new List<ComponentValue>(input);
            int index = rest.FindIndex(t => CssColorParser.IsIdent(t,"in"));
            interpolation=index>=0;
            if (index>=0)
            {
                int count = index+3<rest.Count && CssColorParser.IsIdent(rest[index+3],"hue") ? 4 : 2;
                if (index+count>rest.Count || !CssColorParser.TryInterpolation(rest.GetRange(index,count),out space,out hue)) return false;
                rest.RemoveRange(index,count);
            }
            bool valid = kind switch
            {
                "linear-gradient" => Linear(rest),
                "radial-gradient" => Radial(rest),
                _ => Conic(rest)
            };
            if (!valid || (rest.Count == 0 && index<0)) return false;
            value=rest; return true;
        }
        private static bool Linear(List<ComponentValue> tokens)
        {
            if (tokens.Count == 0) return true;
            if (tokens.Count == 1) return TryCoordinate(tokens[0],"angle-percentage",out CssValue? angle) && angle is not CssPercentageValue && (angle is not CssMathValue math || math.Fits("angle"));
            if (tokens.Count is not (2 or 3) || !CssColorParser.IsIdent(tokens[0],"to")) return false;
            bool horizontal = false, vertical = false;
            for (int i=1;i<tokens.Count;i++)
            {
                if (CssColorParser.IsIdent(tokens[i],"left") || CssColorParser.IsIdent(tokens[i],"right")) { if (horizontal) return false; horizontal=true; }
                else if (CssColorParser.IsIdent(tokens[i],"top") || CssColorParser.IsIdent(tokens[i],"bottom")) { if (vertical) return false; vertical=true; }
                else return false;
            }
            return true;
        }
        private static bool Radial(List<ComponentValue> tokens)
        {
            int at = tokens.FindIndex(t => CssColorParser.IsIdent(t,"at"));
            if (at>=0)
            {
                if (at==tokens.Count-1 || !CssTypeParser.TryParse("position",tokens.GetRange(at+1,tokens.Count-at-1),out _)) return false;
                tokens = tokens.GetRange(0,at);
            }
            string? shape = null, size = null;
            var radii = new List<ComponentValue>();
            foreach (ComponentValue token in tokens)
            {
                if (token is PreservedToken p && p.Token.Kind==CssTokenKind.Ident)
                {
                    string keyword = p.Token.Value.ToLowerInvariant();
                    if (keyword is "circle" or "ellipse") { if (shape!=null) return false; shape=keyword; }
                    else if (keyword is "closest-side" or "farthest-side" or "closest-corner" or "farthest-corner") { if (size!=null) return false; size=keyword; }
                    else return false;
                }
                else radii.Add(token);
            }
            if (radii.Count==0) return true;
            if (size!=null || radii.Count>2 || (shape=="circle" && radii.Count!=1) || (shape=="ellipse" && radii.Count!=2)) return false;
            string type = radii.Count==1 ? "length" : "length-percentage";
            foreach (ComponentValue radius in radii)
            {
                if (!TryCoordinate(radius,type,out CssValue? parsed) || !Nonnegative(parsed!)) return false;
            }
            return true;
        }
        private static bool Nonnegative(CssValue value) => value switch
        {
            CssDimensionValue d => d.Value>=0,
            CssPercentageValue p => p.Value>=0,
            CssMathValue => true,
            _ => false
        };
        private static bool Conic(List<ComponentValue> tokens)
        {
            int index = 0;
            if (tokens.Count>0 && CssColorParser.IsIdent(tokens[0],"from"))
            {
                if (tokens.Count<2 || !TryCoordinate(tokens[1],"angle-percentage",out CssValue? angle) || angle is CssPercentageValue || angle is CssMathValue math && !math.Fits("angle")) return false;
                index=2;
            }
            if (index==tokens.Count) return true;
            return CssColorParser.IsIdent(tokens[index],"at") && index+1<tokens.Count && CssTypeParser.TryParse("position",tokens.GetRange(index+1,tokens.Count-index-1),out _);
        }
    }
}
