using NecronomiDom.Dom;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Math
{
    public static partial class CssMathParser
    {
        private static readonly string[] SizingChannels = { "size" };
        /// <summary>CSS Values 5 § 11 calc-size() grammar; intrinsic size is a context-restricted length symbol.</summary>
        public static bool TryParseSizing(FunctionValue function, out CssValue? value) => TryParseSizing(function, 0, out value);
        private static bool TryParseSizing(FunctionValue function, int depth, out CssValue? value)
        {
            value = null;
            if (!Names.AsciiEqual(function.Name, "calc-size") || depth > 128) return false;
            IReadOnlyList<IReadOnlyList<ComponentValue>> arguments = CssParser.SplitComma(function.Arguments);
            if (arguments.Count != 2) return false;
            IReadOnlyList<ComponentValue> basisTokens = CssParser.Trim(arguments[0]);
            IReadOnlyList<ComponentValue> calculationTokens = CssParser.Trim(arguments[1]);
            if (basisTokens.Count == 0 || calculationTokens.Count == 0) return false;
            CssValue? basis = null;
            if (basisTokens.Count == 1)
            {
                if (basisTokens[0] is PreservedToken p && p.Token.Kind == CssTokenKind.Ident)
                {
                    string keyword = Names.AsciiLower(p.Token.Value);
                    if (keyword is "auto" or "min-content" or "max-content" or "fit-content" or "stretch" or "contain" or "content" or "any" or "-webkit-fill-available")
                        basis = new CssKeywordValue(keyword);
                    else return false;
                }
                else if (basisTokens[0] is FunctionValue nested && Names.AsciiEqual(nested.Name, "calc-size"))
                {
                    if (!TryParseSizing(nested, depth + 1, out basis)) return false;
                }
                else if (basisTokens[0] is FunctionValue fit && Names.AsciiEqual(fit.Name, "fit-content"))
                {
                    if (!CssTypeParser.TryParse("fit-content()", basisTokens, out basis)) return false;
                }
            }
            if (basis == null)
            {
                if (!ParseSizingSum(basisTokens, false, depth, out CssMathValue? numericBasis) || numericBasis == null) return false;
                basis = numericBasis;
            }
            bool any = basis is CssKeywordValue { Value: "any" };
            if (!ParseSizingSum(calculationTokens, !any, depth, out CssMathValue? calculation) || calculation == null) return false;
            value = new CssSizingCalculationValue(basis!, calculation);
            return true;
        }
        private static bool ParseSizingSum(IReadOnlyList<ComponentValue> tokens, bool allowSize, int depth, out CssMathValue? value)
        {
            value = null;
            if (allowSize)
                tokens = Symbolize(tokens, SizingChannels,
                    CssNumericType.For(CssNumericDimension.Length));
            var reader = new Reader(tokens, CssNumericDimension.Length, depth + 1);
            if (!reader.Sum(out CssMathExpression? root) || root == null || !reader.End ||
                !root.Type.Matches(CssNumericDimension.Length, true)) return false;
            value = new CssMathValue(CssMathSimplifier.Simplify(root, CssMathEvaluationContext.Specified), root.Type);
            return true;
        }
    }
}
