using System;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM 1 § 4.2 simple numeric value, stored as a value type rather than a per-frame object.</summary>
    public record struct CSSUnitValue
    {
        public CSSUnitValue(double value, string unit)
        {
            ArgumentNullException.ThrowIfNull(unit);
            unit = unit.ToLowerInvariant();
            string numericUnit = unit == "number" ? "" : unit == "percent" ? "%" : unit;
            if (!CssMathUnits.TryGet(numericUnit, out _, out _)) throw new DomTypeError("Unknown CSS numeric unit.");
            Value = value;
            Unit = unit;
        }
        public double Value { get; set; }
        public string Unit { get; }
        internal string NumericUnit => Unit == "number" ? "" : Unit == "percent" ? "%" : Unit;
        public CSSUnitValue To(string unit)
        {
            var target = new CSSUnitValue(0, unit);
            if (Unit == target.Unit) return new CSSUnitValue(Value, target.Unit);
            CssMathUnits.TryGet(NumericUnit, out var fromDimension, out double? fromScale);
            CssMathUnits.TryGet(target.NumericUnit, out var toDimension, out double? toScale);
            if (fromDimension != toDimension || !fromScale.HasValue || !toScale.HasValue)
                throw new DomTypeError("CSS units cannot be converted without a layout/font basis.");
            return new CSSUnitValue(Value * fromScale.Value / toScale.Value, target.Unit);
        }
        public override string ToString()
        {
            string number = CssSerialization.SerializeNumber(Value);
            return double.IsFinite(Value) ? number + NumericUnit : "calc(" + number + (NumericUnit.Length == 0 ? "" : " * 1" + NumericUnit) + ")";
        }
        public CSSNumericValue Add(CSSNumericValue other) => new CSSNumericValue(this).Add(other);
        public CSSNumericValue Sub(CSSNumericValue other) => new CSSNumericValue(this).Sub(other);
        public CSSNumericValue Mul(CSSNumericValue other) => new CSSNumericValue(this).Mul(other);
        public CSSNumericValue Div(CSSNumericValue other) => new CSSNumericValue(this).Div(other);
        public CSSNumericValue Min(CSSNumericValue other) => new CSSNumericValue(this).Min(other);
        public CSSNumericValue Max(CSSNumericValue other) => new CSSNumericValue(this).Max(other);
        public bool Equals(CSSUnitValue other) => new CSSNumericValue(this).Equals(new CSSNumericValue(other));
        public bool Equals(CSSNumericValue other) => new CSSNumericValue(this).Equals(other);
        public override int GetHashCode()
        {
            CssMathUnits.TryGet(NumericUnit, out var dimension, out double? scale);
            return scale.HasValue ? HashCode.Combine(dimension, Value * scale.Value) : HashCode.Combine(Unit, Value);
        }
        internal CssValue ToCssValue() => Unit == "number" ? new CssNumberValue(Value, Value == System.Math.Truncate(Value))
            : Unit == "percent" ? new CssPercentageValue(Value) : new CssDimensionValue(Value, Unit);
        internal static bool TryFrom(CssValue value, out CSSUnitValue unit)
        {
            switch (value)
            {
                case CssNumberValue number: unit = new CSSUnitValue(number.Value, "number"); return true;
                case CssPercentageValue percentage: unit = new CSSUnitValue(percentage.Value, "percent"); return true;
                case CssDimensionValue dimension: unit = new CSSUnitValue(dimension.Value, dimension.Unit); return true;
                default: unit = default; return false;
            }
        }
    }
    /// <summary>CSS Typed OM 1 § 3.2 keyword value represented without a mutable heap object.</summary>
    public struct CSSKeywordValue
    {
        private string _value;
        public CSSKeywordValue(string value) { _value = ""; Value = value; }
        public string Value
        {
            readonly get => _value;
            set { if (string.IsNullOrEmpty(value)) throw new DomTypeError("A CSS keyword cannot be empty."); _value = value; }
        }
        public override readonly string ToString() => CssSerialization.SerializeIdentifier(Value);
    }
}
