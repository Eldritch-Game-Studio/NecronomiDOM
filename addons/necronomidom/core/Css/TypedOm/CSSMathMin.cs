using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM numeric min, retaining its ordered operand snapshot.</summary>
    public readonly struct CSSMathMin
    {
        public CSSMathMin() : this(System.Array.Empty<CSSNumericValue>()) { }
        public CSSMathMin(params CSSNumericValue[] values) : this(new CSSNumericArray(values)) { }
        public CSSMathMin(IEnumerable<CSSNumericValue> values) : this(new CSSNumericArray(values)) { }
        public CSSMathMin(double first, params double[] rest) : this(CSSMathConstruction.Numbers(first, rest)) { }
        private CSSMathMin(CSSNumericArray values)
        {
            NumericValue = CSSMathConstruction.Create("min", values);
            Values = values;
        }
        public string Operator => "min";
        public CSSNumericArray Values { get; }
        public CSSNumericValue NumericValue { get; }
        public CssNumericType Type => NumericValue.Type;
        public CSSUnitValue To(string unit) => NumericValue.To(unit);
        public bool Equals(CSSNumericValue other) => NumericValue.Equals(other);
        public override string ToString() => CSSMathConstruction.Serialize(NumericValue, true);
        public static implicit operator CSSNumericValue(CSSMathMin value) => value.NumericValue;
    }
}
