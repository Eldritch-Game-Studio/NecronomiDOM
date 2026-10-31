using NecronomiDom.Dom;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Math
{
    public static partial class CssMathParser
    {
        private static bool ParseTypedRoot(FunctionValue function, CssNumericDimension? hint,
            out CssMathExpression? root, out bool rawPercentage)
        {
            rawPercentage = false;
            if (hint == CssNumericDimension.Number && ParseFunction(function, null, 0, out root) &&
                root != null && root.Type.IsPercentage)
            {
                rawPercentage = true;
                return true;
            }
            if (ParseFunction(function, hint, 0, out root) && root != null && root.Type.IsSimple) return true;
            if (hint.HasValue || !ContainsPercentage(function.Arguments) || !ContainsDimension(function.Arguments)) return false;
            for (CssNumericDimension dimension = CssNumericDimension.Length; dimension < CssNumericDimension.Percentage; dimension++)
                if (ParseFunction(function, dimension, 0, out root) && root != null && root.Type.IsSimple) return true;
            return false;
        }
        private static bool ContainsPercentage(IReadOnlyList<ComponentValue> values)
        {
            foreach (ComponentValue value in values)
            {
                if (value is PreservedToken { Token.Kind: CssTokenKind.Percentage }) return true;
                if (value is FunctionValue attribute && Names.AsciiEqual(attribute.Name, "attr") &&
                    CssMathNumericLeaf.TryParse(attribute, null, out CssMathExpression? leaf) && leaf!.Type.Percentage != 0) return true;
                if (value is FunctionValue function && ContainsPercentage(function.Arguments)) return true;
                if (value is SimpleBlock block && ContainsPercentage(block.Value)) return true;
            }
            return false;
        }
        private static bool ContainsDimension(IReadOnlyList<ComponentValue> values)
        {
            foreach (ComponentValue value in values)
            {
                if (value is PreservedToken { Token.Kind: CssTokenKind.Dimension } token &&
                    CssMathUnits.TryGet(token.Token.Unit, out _, out _)) return true;
                if (value is FunctionValue function)
                {
                    if (CssMathNumericLeaf.TryParse(function, null, out CssMathExpression? leaf) &&
                        leaf!.Type.IsSimple && !leaf.Type.IsNumber && !leaf.Type.IsPercentage) return true;
                    if (ContainsDimension(function.Arguments)) return true;
                }
                if (value is SimpleBlock block && ContainsDimension(block.Value)) return true;
            }
            return false;
        }
    }
}
