using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM numeric max, retaining its ordered operand snapshot.</summary>
    public readonly struct CSSMathMax
    {
        public CSSMathMax() : this(System.Array.Empty<CSSNumericValue>()) { }
        public CSSMathMax(params CSSNumericValue[] values) : this(new CSSNumericArray(values)) { }
        public CSSMathMax(IEnumerable<CSSNumericValue> values) : this(new CSSNumericArray(values)) { }
        public CSSMathMax(double first, params double[] rest) : this(CSSMathConstruction.Numbers(first, rest)) { }
        private CSSMathMax(CSSNumericArray values)
        {
            NumericValue = CSSMathConstruction.Create("max", values);
            Values = values;
        }
        public string Operator => "max";
        public CSSNumericArray Values { get; }
        public CSSNumericValue NumericValue { get; }
        public CssNumericType Type => NumericValue.Type;
        public CSSUnitValue To(string unit) => NumericValue.To(unit);
        public bool Equals(CSSNumericValue other) => NumericValue.Equals(other);
        public override string ToString() => CSSMathConstruction.Serialize(NumericValue, true);
        public static implicit operator CSSNumericValue(CSSMathMax value) => value.NumericValue;
    }
}
