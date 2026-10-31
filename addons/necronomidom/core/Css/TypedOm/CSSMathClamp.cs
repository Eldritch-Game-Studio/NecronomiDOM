using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM clamp with independently preserved lower, value, and upper operands.</summary>
    public readonly struct CSSMathClamp
    {
        public CSSMathClamp() => throw new NecronomiDom.Dom.DomTypeError("CSSMathClamp requires three operands.");
        public CSSMathClamp(CSSNumericValue lower, CSSNumericValue value, CSSNumericValue upper)
        {
            Lower = lower;
            Value = value;
            Upper = upper;
            NumericValue = CSSMathConstruction.Create("clamp", new CSSNumericArray(new[] { lower, value, upper }, true));
        }
        public CSSMathClamp(double lower, double value, double upper)
            : this(new CSSUnitValue(lower, "number"), new CSSUnitValue(value, "number"), new CSSUnitValue(upper, "number")) { }
        public string Operator => "clamp";
        public CSSNumericValue Lower { get; }
        public CSSNumericValue Value { get; }
        public CSSNumericValue Upper { get; }
        public CSSNumericValue NumericValue { get; }
        public CssNumericType Type => NumericValue.Type;
        public CSSUnitValue To(string unit) => NumericValue.To(unit);
        public bool Equals(CSSNumericValue other) => NumericValue.Equals(other);
        public override string ToString() => CSSMathConstruction.Serialize(NumericValue, true);
        public static implicit operator CSSNumericValue(CSSMathClamp value) => value.NumericValue;
    }
}
