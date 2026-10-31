using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    // These declaration-owned slots are deliberately mutable; ordinary parser values remain immutable.
    internal sealed class TypedNumberSlot : CssNumberValue
    {
        internal TypedNumberSlot(CSSUnitValue value) : base(0, false) => Current = value;
        internal CSSUnitValue Current;
        public override double Value => Current.Value;
        public override bool IsInteger => Value == System.Math.Truncate(Value);
    }
    internal sealed class TypedDimensionSlot : CssDimensionValue
    {
        internal TypedDimensionSlot(CSSUnitValue value) : base(0, "px") => Current = value;
        internal CSSUnitValue Current;
        public override double Value => Current.Value;
        public override string Unit => Current.NumericUnit;
    }
    internal sealed class TypedPercentageSlot : CssPercentageValue
    {
        internal TypedPercentageSlot(CSSUnitValue value) : base(0) => Current = value;
        internal CSSUnitValue Current;
        public override double Value => Current.Value;
    }
    internal sealed class TypedMathSlot : CssMathValue
    {
        internal TypedMathSlot(CSSNumericValue value) : base(value.Expression, value.Type) => Current = value;
        internal CSSNumericValue Current;
        public override CssMathExpression Root => Current.Expression;
        public override CssNumericType Type => Current.Type;
    }
}
