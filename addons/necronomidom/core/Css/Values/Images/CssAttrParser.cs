using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Images
{
    /// <summary>CSS Values 5 attr() typed syntax as shipped in Chromium, including registration syntax validation.</summary>
    public static class CssAttrParser
    {
        public static bool TryParse(FunctionValue function,out CssValue? value)
        {
            value = null;
            if (!function.Name.Equals("attr",StringComparison.OrdinalIgnoreCase)) return false;
            List<ComponentValue> tokens = TokenText.WithoutWhitespace(function.Arguments);
            int comma = tokens.FindIndex(t => t is PreservedToken p && p.Token.Kind == CssTokenKind.Comma);
            int end = comma<0 ? tokens.Count : comma;
            if (end is <1 or >2 || tokens[0] is not PreservedToken { Token.Kind: CssTokenKind.Ident } name) return false;
            string? unit=null, syntax=null;
            if (end==2)
            {
                if (tokens[1] is FunctionValue type && type.Name.Equals("type",StringComparison.OrdinalIgnoreCase))
                {
                    if (!TrySyntax(type.Arguments,out syntax)) return false;
                }
                else if (tokens[1] is PreservedToken token)
                {
                    if (token.Token.IsDelim('%')) unit = "%";
                    else if (token.Token.Kind == CssTokenKind.Ident)
                    {
                        string keyword = token.Token.Value.ToLowerInvariant();
                        if (keyword == "raw-string") unit = null;
                        else if (keyword == "number" || CssTypeParser.UnitType(keyword)!=null) unit = keyword=="q" ? "Q" : keyword;
                        else return false;
                    }
                    else return false;
                }
                else return false;
            }
            IReadOnlyList<ComponentValue>? fallback = null;
            if (comma>=0)
            {
                var original=new List<ComponentValue>(function.Arguments);
                int separator=original.FindIndex(t=>t is PreservedToken p && p.Token.Kind==CssTokenKind.Comma);
                fallback=TokenText.Trim(original.GetRange(separator+1,original.Count-separator-1));
                if (!ValidValueTokens(fallback)) return false;
            }
            value = new CssAttrValue(name.Token.Value,unit,syntax,fallback); return true;
        }
        private static bool TrySyntax(IReadOnlyList<ComponentValue> input,out string? syntax)
        {
            syntax=null;
            List<ComponentValue> tokens = TokenText.WithoutWhitespace(input);
            if (tokens.Count==1 && tokens[0] is PreservedToken star && star.Token.IsDelim('*')) { syntax="*"; return true; }
            var builder = new StringBuilder();
            int index=0;
            while (index<tokens.Count)
            {
                if (index>0) builder.Append(" | ");
                if (tokens[index] is not PreservedToken first) return false;
                if (first.Token.IsDelim('<'))
                {
                    if (index+2>=tokens.Count || tokens[index+1] is not PreservedToken { Token.Kind: CssTokenKind.Ident } type || tokens[index+2] is not PreservedToken close || !close.Token.IsDelim('>')) return false;
                    string name = type.Token.Value;
                    builder.Append('<').Append(name).Append('>'); index+=3;
                }
                else if (first.Token.Kind==CssTokenKind.Ident && !CssWideKeyword.IsWideKeyword(first.Token.Value) && !first.Token.IsIdent("default"))
                { builder.Append(CssSerialization.SerializeIdentifier(first.Token.Value)); index++; }
                else return false;
                if (index<tokens.Count && tokens[index] is PreservedToken multiplier && (multiplier.Token.IsDelim('+') || multiplier.Token.IsDelim('#')))
                { builder.Append(multiplier.Token.Value); index++; }
                if (index==tokens.Count) break;
                if (tokens[index] is not PreservedToken separator || !separator.Token.IsDelim('|') || ++index==tokens.Count) return false;
            }
            if (builder.Length==0) return false;
            syntax = builder.ToString(); return CssRegistrationSyntax.IsValid(syntax,CssSyntaxDefinitionContext.Attribute);
        }
        internal static bool ValidValueTokens(IReadOnlyList<ComponentValue> tokens,bool topLevel=true)
        {
            foreach (ComponentValue token in tokens)
            {
                if (token is PreservedToken p && (p.Token.Kind is CssTokenKind.BadString or CssTokenKind.BadUrl || topLevel && p.Token.Kind==CssTokenKind.Semicolon)) return false;
                if (token is FunctionValue f && !ValidValueTokens(f.Arguments,false)) return false;
                if (token is SimpleBlock b && !ValidValueTokens(b.Value,false)) return false;
            }
            return true;
        }
    }
}
