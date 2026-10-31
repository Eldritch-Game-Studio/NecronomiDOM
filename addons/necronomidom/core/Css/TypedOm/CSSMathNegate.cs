using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM numeric negate, preserving its operand and dimensional type.</summary>
    public readonly struct CSSMathNegate
    {
        public CSSMathNegate() => throw new NecronomiDom.Dom.DomTypeError("CSSMathNegate requires an operand.");
        public CSSMathNegate(CSSNumericValue value)
        {
            Value = value;
            NumericValue = CSSMathConstruction.Unary("negate", value);
        }
        public CSSMathNegate(double value) : this(new CSSUnitValue(value, "number")) { }
        public string Operator => "negate";
        public CSSNumericValue Value { get; }
        public CSSNumericValue NumericValue { get; }
        public CssNumericType Type => NumericValue.Type;
        public CSSUnitValue To(string unit) => NumericValue.To(unit);
        public bool Equals(CSSNumericValue other) => NumericValue.Equals(other);
        public override string ToString() => CSSMathConstruction.Serialize(NumericValue);
        public static implicit operator CSSNumericValue(CSSMathNegate value) => value.NumericValue;
    }
}
