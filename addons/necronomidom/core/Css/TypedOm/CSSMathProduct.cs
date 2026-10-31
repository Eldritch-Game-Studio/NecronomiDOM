using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM numeric product, retaining its ordered operand snapshot.</summary>
    public readonly struct CSSMathProduct
    {
        public CSSMathProduct() : this(System.Array.Empty<CSSNumericValue>()) { }
        public CSSMathProduct(params CSSNumericValue[] values) : this(new CSSNumericArray(values)) { }
        public CSSMathProduct(IEnumerable<CSSNumericValue> values) : this(new CSSNumericArray(values)) { }
        public CSSMathProduct(double first, params double[] rest) : this(CSSMathConstruction.Numbers(first, rest)) { }
        private CSSMathProduct(CSSNumericArray values)
        {
            NumericValue = CSSMathConstruction.Create("*", values);
            Values = values;
        }
        public string Operator => "product";
        public CSSNumericArray Values { get; }
        public CSSNumericValue NumericValue { get; }
        public CssNumericType Type => NumericValue.Type;
        public CSSUnitValue To(string unit) => NumericValue.To(unit);
        public bool Equals(CSSNumericValue other) => NumericValue.Equals(other);
        public override string ToString() => CSSMathConstruction.Serialize(NumericValue);
        public static implicit operator CSSNumericValue(CSSMathProduct value) => value.NumericValue;
    }
}
