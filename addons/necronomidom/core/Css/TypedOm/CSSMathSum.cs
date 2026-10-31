using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM numeric sum, retaining its ordered operand snapshot.</summary>
    public readonly struct CSSMathSum
    {
        public CSSMathSum() : this(System.Array.Empty<CSSNumericValue>()) { }
        public CSSMathSum(params CSSNumericValue[] values) : this(new CSSNumericArray(values)) { }
        public CSSMathSum(IEnumerable<CSSNumericValue> values) : this(new CSSNumericArray(values)) { }
        public CSSMathSum(double first, params double[] rest) : this(CSSMathConstruction.Numbers(first, rest)) { }
        private CSSMathSum(CSSNumericArray values)
        {
            NumericValue = CSSMathConstruction.Create("+", values);
            Values = values;
        }
        public string Operator => "sum";
        public CSSNumericArray Values { get; }
        public CSSNumericValue NumericValue { get; }
        public CssNumericType Type => NumericValue.Type;
        public CSSUnitValue To(string unit) => NumericValue.To(unit);
        public bool Equals(CSSNumericValue other) => NumericValue.Equals(other);
        public override string ToString() => CSSMathConstruction.Serialize(NumericValue);
        public static implicit operator CSSNumericValue(CSSMathSum value) => value.NumericValue;
    }
}
