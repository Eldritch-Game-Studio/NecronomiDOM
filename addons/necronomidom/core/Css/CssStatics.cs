using NecronomiDom.Css.Media;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css
{
    /// <summary>CSSOM § 8.1 CSS.escape and CSS Conditional 3 CSS.supports entry points.</summary>
    public static class CssStatics
    {
        public static string Escape(string value) => CssSerialization.SerializeIdentifier(value);
        public static bool Supports(string property, string value)
        {
            if (!property.StartsWith("--", System.StringComparison.Ordinal) && CssPropertyRegistry.Get(property) == null) return false;
            var declaration = CssParser.ParseDeclaration(CssSerialization.SerializeIdentifier(property) + ":" + value);
            return declaration != null && !declaration.Important && CssPropertyParser.Parse(declaration.Name, declaration.Value) != null;
        }
        public static bool Supports(string condition)
        {
            // CSS.supports(conditionText) accepts a bare declaration as well as the @supports condition grammar.
            var parsed = SupportsParser.Parse(condition) ?? SupportsParser.Parse("(" + condition + ")");
            return parsed != null && MediaEvaluator.Supports(parsed);
        }
    }
}
