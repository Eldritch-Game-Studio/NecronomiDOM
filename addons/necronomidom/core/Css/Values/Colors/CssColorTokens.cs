using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Colors
{
    internal static class CssColorTokens
    {
        internal static bool IsAsciiName(string text)
        {
            foreach (char character in text) if (character>127) return false;
            return true;
        }
        internal static ComponentValue Canonical(ComponentValue component)
        {
            if (component is PreservedToken p)
            {
                CssToken token=p.Token;
                if (token.Kind==CssTokenKind.Ident) token=token with { Value=token.Value.ToLowerInvariant() };
                if (token.Kind is CssTokenKind.Number or CssTokenKind.Dimension or CssTokenKind.Percentage)
                    token=token with { Representation="", Unit=token.Unit.Equals("q",System.StringComparison.OrdinalIgnoreCase) ? "Q" : token.Unit.ToLowerInvariant() };
                return new PreservedToken(token);
            }
            if (component is FunctionValue function)
                return new FunctionValue(function.NameToken with { Value=function.Name.ToLowerInvariant() },Canonical(function.Arguments));
            if (component is SimpleBlock block) return new SimpleBlock(block.OpenerToken,Canonical(block.Value));
            return component;
        }
        internal static IReadOnlyList<ComponentValue> Canonical(IReadOnlyList<ComponentValue> tokens)
        {
            var result=new List<ComponentValue>(tokens.Count);
            foreach (ComponentValue token in tokens) result.Add(Canonical(token));
            return result;
        }
    }
}
