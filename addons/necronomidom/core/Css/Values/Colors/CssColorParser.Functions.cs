using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Colors
{
    public static partial class CssColorParser
    {
        private static bool TryFunctional(FunctionValue f,out CssValue? value)
        {
            value = null;
            string space = f.Name.ToLowerInvariant() switch { "rgba" => "rgb", "hsla" => "hsl", var n => n };
            List<ComponentValue> tokens = TokenText.WithoutWhitespace(f.Arguments);
            CssColorValue? origin = null;
            int offset = 0;
            if (tokens.Count > 0 && IsIdent(tokens[0],"from"))
            {
                if (tokens.Count < 2 || !TryParse(tokens[1],out CssValue? parsed) || parsed is not CssColorValue color) return false;
                origin = color; offset = 2;
            }
            if (space == "color")
            {
                if (tokens.Count <= offset || tokens[offset] is not PreservedToken p || p.Token.Kind != CssTokenKind.Ident) return false;
                if (!CssColorTokens.IsAsciiName(p.Token.Value)) return false;
                space = p.Token.Value.ToLowerInvariant(); offset++;
                if (!IsSpace(space)) return false;
                if (space == "xyz") space = "xyz-d65";
            }
            var channels = new List<ComponentValue>();
            bool comma = false, slash = false;
            for (int i = offset; i < tokens.Count; i++)
            {
                ComponentValue token = tokens[i];
                if (token is PreservedToken p && p.Token.Kind == CssTokenKind.Comma)
                {
                    if (origin != null || space is not ("rgb" or "hsl") || slash || channels.Count == 0 || (i > offset && tokens[i-1] is PreservedToken previous && previous.Token.Kind == CssTokenKind.Comma)) return false;
                    comma = true; continue;
                }
                if (IsSlash(token))
                {
                    if (comma || slash || channels.Count != 3) return false;
                    slash = true; continue;
                }
                channels.Add(token);
            }
            if (channels.Count is not (3 or 4) || (!comma && (channels.Count == 4) != slash)) return false;
            if (comma)
            {
                if (tokens.Count-offset != channels.Count*2-1) return false;
                for (int i = 1; i < tokens.Count-offset; i += 2)
                    if (tokens[offset+i] is not PreservedToken separator || separator.Token.Kind != CssTokenKind.Comma) return false;
                bool? percentage = null;
                for (int i = 0; i < channels.Count; i++)
                {
                    if (IsIdent(channels[i],"none")) return false;
                    if (space == "rgb" && i<3)
                    {
                        bool percent = CssTypeParser.TryParse("percentage",new[] { channels[i] },out _);
                        if ((!percent && !CssTypeParser.TryParse("number",new[] { channels[i] },out _)) || (percentage.HasValue && percentage != percent)) return false;
                        percentage = percent;
                    }
                    else if (space == "hsl" && i is 1 or 2 && !CssTypeParser.TryParse("percentage",new[] { channels[i] },out _)) return false;
                }
            }
            IReadOnlyDictionary<string,double>? symbols = origin == null ? null : CssColorChannels.Symbols(new(space,0,0,0,1),space);
            for (int i = 0; i < channels.Count; i++)
            {
                if (!CssColorChannels.TryRead(channels[i],space,i,symbols,out _,validateOnly:true)) return false;
                channels[i]=CssColorTokens.Canonical(channels[i]);
            }
            var functional = new CssRelativeColorValue(space,origin,channels);
            if (origin != null) { value = functional; return true; }
            if (!functional.TryResolve(new CssColorContext(),out CssResolvedColor resolved)) { value = functional; return true; }
            value = new CssAbsoluteColorValue(resolved,null,space is "rgb" or "hsl" or "hwb" && resolved.Missing==0) { SpecifiedFunction = functional };
            return true;
        }
    }
}
