using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Css.Values.Images
{
    public static partial class CssImageParser
    {
        private static CssValue? CanonicalPrelude(List<ComponentValue> tokens,string kind,string space,string hue,string defaultSpace)
        {
            int at=tokens.FindIndex(t=>CssColorParser.IsIdent(t,"at"));
            if (at>=0 && Center(tokens,at+1)) tokens.RemoveRange(at,tokens.Count-at);
            else if (at>=0) CanonicalPosition(tokens,at+1);
            if (kind=="linear-gradient")
            {
                if (tokens.Count==2 && CssColorParser.IsIdent(tokens[0],"to") && CssColorParser.IsIdent(tokens[1],"bottom")) tokens.Clear();
                if (tokens.Count==3 && (CssColorParser.IsIdent(tokens[1],"top") || CssColorParser.IsIdent(tokens[1],"bottom")))
                    (tokens[1],tokens[2])=(tokens[2],tokens[1]);
                if (tokens.Count==1 && CssColorChannels.TryRead(tokens[0],"hsl",0,null,out double angle) && CssColorConversion.Hue(angle)==180) tokens.Clear();
            }
            else if (kind=="radial-gradient")
            {
                at=tokens.FindIndex(t=>CssColorParser.IsIdent(t,"at"));
                int end=at<0 ? tokens.Count : at;
                int shape=tokens.FindIndex(0,end,t=>CssColorParser.IsIdent(t,"circle") || CssColorParser.IsIdent(t,"ellipse"));
                if (shape>=0)
                {
                    ComponentValue token=tokens[shape]; tokens.RemoveAt(shape);
                    if (CssColorParser.IsIdent(token,"circle") && (end==1 || tokens[0] is PreservedToken { Token.Kind:CssTokenKind.Ident })) tokens.Insert(0,token);
                }
                int defaultSize=tokens.FindIndex(t=>CssColorParser.IsIdent(t,"farthest-corner"));
                if (defaultSize>=0) tokens.RemoveAt(defaultSize);
            }
            else if (tokens.Count>=2 && CssColorParser.IsIdent(tokens[0],"from") && CssColorChannels.TryRead(tokens[1],"hsl",0,null,out double conicAngle) && CssColorConversion.Hue(conicAngle)==0)
                tokens.RemoveRange(0,2);
            if (space!=defaultSpace || hue!="shorter")
            {
                tokens.Add(Ident("in")); tokens.Add(Ident(space));
                if (hue!="shorter") { tokens.Add(Ident(hue)); tokens.Add(Ident("hue")); }
            }
            return tokens.Count==0 ? null : CssValueParser.Parse(CssColorTokens.Canonical(tokens));
        }
        private static void CanonicalPosition(List<ComponentValue> tokens,int start)
        {
            int count=tokens.Count-start;
            if (!Vertical(tokens[start])) return;
            if (count==2 && Horizontal(tokens[start+1]))
                (tokens[start],tokens[start+1])=(tokens[start+1],tokens[start]);
            else if (count==4 && Horizontal(tokens[start+2]))
            {
                (tokens[start],tokens[start+2])=(tokens[start+2],tokens[start]);
                (tokens[start+1],tokens[start+3])=(tokens[start+3],tokens[start+1]);
            }
        }
        private static bool Vertical(ComponentValue value) => CssColorParser.IsIdent(value,"top") || CssColorParser.IsIdent(value,"bottom");
        private static bool Horizontal(ComponentValue value) => CssColorParser.IsIdent(value,"left") || CssColorParser.IsIdent(value,"right") || CssColorParser.IsIdent(value,"center");
        private static bool Center(List<ComponentValue> tokens,int start)
        {
            int count=tokens.Count-start;
            if (count is <1 or >2) return false;
            for (int i=start;i<tokens.Count;i++)
                if (!CssColorParser.IsIdent(tokens[i],"center") && tokens[i] is not PreservedToken { Token.Kind: CssTokenKind.Percentage, Token.Number: 50 }) return false;
            return true;
        }
        private static ComponentValue Ident(string keyword) => new PreservedToken(new CssToken(CssTokenKind.Ident,keyword));
    }
}
