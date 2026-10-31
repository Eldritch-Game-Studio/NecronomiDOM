using NecronomiDom.Dom;
using System.Text;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Images;

namespace NecronomiDom.Css.Values.Math
{
    /// <summary>Values 4 § 10.10 numeric leaves and Values 5 tree-counting functions, validated by their own grammars.</summary>
    public sealed class CssMathNumericLeaf : CssMathExpression
    {
        public CssMathNumericLeaf(CssValue value, CssNumericType type) : base(type) => Value = value;
        public CssValue Value { get; }
        public override bool TryEvaluate(CssMathEvaluationContext context, out double value)
        {
            if (Value is CssKeywordValue { Value: "size" } && Type.IsDimension(CssNumericDimension.Length))
            {
                value = context.IntrinsicSize.GetValueOrDefault();
                return context.IntrinsicSize.HasValue;
            }
            double? resolved = context.ResolveNumericValue?.Invoke(Value);
            value = resolved.GetValueOrDefault();
            return resolved.HasValue;
        }
        internal override void AppendTo(StringBuilder builder, int precedence) => Value.AppendTo(builder);
        internal static bool TryParse(FunctionValue function, CssNumericDimension? hint, out CssMathExpression? expression)
        {
            expression = null;
            string name = Names.AsciiLower(function.Name);
            CssValue? value;
            CssNumericType type;
            if (name is "sibling-index" or "sibling-count")
            {
                if (CssParser.Trim(function.Arguments).Count != 0) return false;
                value = new CssFunctionValue(name, null); type = default;
            }
            else if (name is "anchor" or "anchor-size")
            {
                if (!CssTypeParser.TryParse(name + "()", new ComponentValue[] { function }, out value) || value == null) return false;
                type = CssNumericType.For(CssNumericDimension.Length);
            }
            else if (name == "attr")
            {
                if (!CssAttrParser.TryParse(function, out value) || value is not CssAttrValue attribute) return false;
                if (attribute.Unit != null)
                {
                    if (attribute.Unit == "number") type = default;
                    else if (CssMathUnits.TryGet(attribute.Unit, out CssNumericDimension dimension, out _)) type = CssNumericType.For(dimension);
                    else return false;
                }
                else if (!TrySyntaxType(attribute.Syntax, out type)) return false;
                if (type.Percentage != 0) type = type.Hint(hint ?? CssNumericDimension.Percentage);
            }
            else return false;
            expression = new CssMathNumericLeaf(value!, type);
            return true;
        }
        private static bool TrySyntaxType(string? syntax, out CssNumericType type)
        {
            type = default;
            switch (syntax?.Trim())
            {
                case "<number>": case "<integer>": return true;
                case "<length>": type = CssNumericType.For(CssNumericDimension.Length); return true;
                case "<length-percentage>": type = CssNumericType.For(CssNumericDimension.Length).Hint(CssNumericDimension.Length); return true;
                case "<angle>": type = CssNumericType.For(CssNumericDimension.Angle); return true;
                case "<time>": type = CssNumericType.For(CssNumericDimension.Time); return true;
                case "<frequency>": type = CssNumericType.For(CssNumericDimension.Frequency); return true;
                case "<resolution>": type = CssNumericType.For(CssNumericDimension.Resolution); return true;
                case "<percentage>": type = CssNumericType.For(CssNumericDimension.Percentage); return true;
                default: return false;
            }
        }
    }
}
