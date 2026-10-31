using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Values
{
    internal static class CssUrlParser
    {
        // CSS Values 5 §4.1.1; each step sets its request field, so a repeated modifier's last value wins.
        internal static bool TryParse(FunctionValue function, out CssValue? value)
        {
            value = null;
            List<ComponentValue> args = TokenText.WithoutWhitespace(function.Arguments);
            if (args.Count == 0 || args[0] is not PreservedToken { Token.Kind: CssTokenKind.String } url) return false;
            string? crossOrigin = null, integrity = null, referrerPolicy = null;
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i] is not FunctionValue modifier) return false;
                List<ComponentValue> terms = TokenText.WithoutWhitespace(modifier.Arguments);
                if (terms.Count != 1 || terms[0] is not PreservedToken argument) return false;
                if (Names.AsciiEqual(modifier.Name, "integrity"))
                {
                    if (argument.Token.Kind != CssTokenKind.String) return false;
                    integrity = argument.Token.Value;
                }
                else
                {
                    if (argument.Token.Kind != CssTokenKind.Ident) return false;
                    string keyword = Names.AsciiLower(argument.Token.Value);
                    if (Names.AsciiEqual(modifier.Name, "cross-origin"))
                    {
                        if (keyword is not ("anonymous" or "use-credentials")) return false;
                        crossOrigin = keyword;
                    }
                    else if (Names.AsciiEqual(modifier.Name, "referrer-policy"))
                    {
                        if (keyword is not ("no-referrer" or "no-referrer-when-downgrade" or "same-origin" or "origin"
                            or "strict-origin" or "origin-when-cross-origin" or "strict-origin-when-cross-origin" or "unsafe-url")) return false;
                        referrerPolicy = keyword;
                    }
                    else return false;
                }
            }
            value = new CssUrlValue(url.Token.Value, crossOrigin, integrity, referrerPolicy);
            return true;
        }
    }
}
