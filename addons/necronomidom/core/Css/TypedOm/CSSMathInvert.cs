using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM numeric invert, preserving its operand and dimensional type.</summary>
    public readonly struct CSSMathInvert
    {
        public CSSMathInvert() => throw new NecronomiDom.Dom.DomTypeError("CSSMathInvert requires an operand.");
        public CSSMathInvert(CSSNumericValue value)
        {
            Value = value;
            NumericValue = CSSMathConstruction.Unary("invert", value);
        }
        public CSSMathInvert(double value) : this(new CSSUnitValue(value, "number")) { }
        public string Operator => "invert";
        public CSSNumericValue Value { get; }
        public CSSNumericValue NumericValue { get; }
        public CssNumericType Type => NumericValue.Type;
        public CSSUnitValue To(string unit) => NumericValue.To(unit);
        public bool Equals(CSSNumericValue other) => NumericValue.Equals(other);
        public override string ToString() => CSSMathConstruction.Serialize(NumericValue);
        public static implicit operator CSSNumericValue(CSSMathInvert value) => value.NumericValue;
    }
}
