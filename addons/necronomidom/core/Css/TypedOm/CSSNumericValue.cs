using System;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>Typed OM 1 numeric expression adapter reusing Values 4's dimension algebra and evaluation.</summary>
    public readonly partial struct CSSNumericValue
    {
        private readonly CssMathExpression? _expression;
        private readonly CSSUnitValue _unit;
        private readonly bool _parsedCalc;
        public CSSNumericValue(CSSUnitValue unit) { _unit = unit; _expression = null; _parsedCalc = false; }
        internal CSSNumericValue(CssMathExpression expression, bool parsedCalc = false) { _expression = expression; _unit = default; _parsedCalc = parsedCalc; }
        internal bool IsInitialized => _expression != null || _unit.Unit != null;
        public CssNumericType Type
        {
            get { if (_expression != null) return _expression.Type; CssMathUnits.TryGet(_unit.NumericUnit, out var dimension, out _); return CssNumericType.For(dimension); }
        }
        internal CssMathExpression Expression => _expression ?? new CssMathLiteral(_unit.Value, _unit.NumericUnit, Type);
        public bool TryGetUnit(out CSSUnitValue unit)
        {
            if (_expression == null) { unit = _unit; return true; }
            if (_expression is CssMathLiteral literal && !literal.IsUnbounded)
            {
                unit = new CSSUnitValue(literal.Value, literal.Unit.Length == 0 ? "number" : literal.Unit == "%" ? "percent" : literal.Unit);
                return true;
            }
            unit = default; return false;
        }
        internal static bool TryFrom(CssValue value, out CSSNumericValue numeric)
        {
            if (value is CssMathValue math) { numeric = new CSSNumericValue(math.Root, math.WasCalc); return true; }
            if (CSSUnitValue.TryFrom(value, out var unit)) { numeric = new CSSNumericValue(unit); return true; }
            numeric = default; return false;
        }
        public static CSSNumericValue Parse(string text)
        {
            var tokens = CssParser.ParseComponentValueList(text);
            if (!ReifiableTokens(tokens)) throw DomException.Syntax("The math expression cannot be represented by CSS Typed OM.");
            var value = CssValueParser.Parse(tokens);
            if (value is CssMathValue math)
            {
                if (!ReifiableExpression(math.Root)) throw DomException.Syntax("The math expression cannot be represented by CSS Typed OM.");
                var root = CssMathSimplifier.Simplify(math.Root, CssMathEvaluationContext.Specified);
                if (!math.WasCalc && root is CssMathLiteral literal) return new CSSNumericValue(Unit(literal));
                return new CSSNumericValue(root, math.WasCalc);
            }
            if (value != null && CSSUnitValue.TryFrom(value, out var unit)) return new CSSNumericValue(unit);
            throw DomException.Syntax("The text is not a CSS numeric value.");
        }
        public CSSUnitValue To(string unit)
        {
            if (_expression == null) return _unit.To(unit);
            var simplified = CssMathSimplifier.Simplify(_expression, CssMathEvaluationContext.Specified);
            if (simplified is CssMathLiteral literal && !literal.IsUnbounded)
                return new CSSUnitValue(literal.Value, literal.Unit.Length == 0 ? "number" : literal.Unit == "%" ? "percent" : literal.Unit).To(unit);
            var destination = new CSSUnitValue(0, unit);
            CssMathUnits.TryGet(destination.NumericUnit, out var dimension, out double? scale);
            if (!Type.IsDimension(dimension) || !scale.HasValue && destination.Unit != "percent")
                throw new DomTypeError("The numeric expression cannot be converted to the requested unit.");
            var context = new CssMathEvaluationContext(percentageBasis: destination.Unit == "percent" ? 100 : null);
            if (!_expression.TryEvaluate(context, out double value))
                throw new DomTypeError("Numeric conversion requires an unavailable layout or font basis.");
            return new CSSUnitValue(value / (scale ?? 1), destination.Unit);
        }
        public CSSNumericValue Add(CSSNumericValue other) => Combine("+", other);
        public CSSNumericValue Sub(CSSNumericValue other) => Combine("-", other);
        public CSSNumericValue Mul(CSSNumericValue other) => Combine("*", other);
        public CSSNumericValue Div(CSSNumericValue other) => Combine("/", other);
        public CSSNumericValue Min(CSSNumericValue other) => Combine("min", other);
        public CSSNumericValue Max(CSSNumericValue other) => Combine("max", other);
        private CSSNumericValue Combine(string operation, CSSNumericValue other)
        {
            bool compatible = operation is "*" or "/" ? CssNumericType.Multiply(Type, other.Type, operation == "/", out var type)
                : CssNumericType.Add(Type, other.Type, out type);
            if (!compatible) throw new DomTypeError("CSS numeric dimensions are incompatible.");
            return new CSSNumericValue(new CssMathOperation(operation, new[] { Expression, other.Expression }, type));
        }
        public bool Equals(CSSNumericValue other)
        {
            if (Type != other.Type) return false;
            if (_expression == null && other._expression == null)
            {
                if (_unit.Unit == other._unit.Unit) return _unit.Value == other._unit.Value;
                CssMathUnits.TryGet(_unit.NumericUnit, out var dimension, out _);
                try { return _unit.To(CssMathUnits.Canonical(dimension) is "%" ? "percent" : dimension == CssNumericDimension.Number ? "number" : CssMathUnits.Canonical(dimension)).Value
                    == other._unit.To(CssMathUnits.Canonical(dimension) is "%" ? "percent" : dimension == CssNumericDimension.Number ? "number" : CssMathUnits.Canonical(dimension)).Value; }
                catch (DomTypeError) { return false; }
            }
            var left = _expression == null ? null : CssMathSimplifier.Simplify(_expression, CssMathEvaluationContext.Specified);
            var right = other._expression == null ? null : CssMathSimplifier.Simplify(other._expression, CssMathEvaluationContext.Specified);
            if (left == null || left is CssMathLiteral)
                if (right == null || right is CssMathLiteral)
                {
                    var a = left is CssMathLiteral la ? new CSSUnitValue(la.Value, la.Unit.Length == 0 ? "number" : la.Unit == "%" ? "percent" : la.Unit) : _unit;
                    var b = right is CssMathLiteral lb ? new CSSUnitValue(lb.Value, lb.Unit.Length == 0 ? "number" : lb.Unit == "%" ? "percent" : lb.Unit) : other._unit;
                    return new CSSNumericValue(a).Equals(new CSSNumericValue(b));
                }
            return ToString() == other.ToString();
        }
        internal CssValue ToCssValue() => _expression == null ? _unit.ToCssValue() : new CssMathValue(_expression, Type);
        public override string ToString()
        {
            if (_expression == null) return _unit.ToString();
            var text = new System.Text.StringBuilder();
            bool wrap = _expression is CssMathLiteral || _expression is CssMathOperation op && CssMathSerialization.IsArithmetic(op.Name);
            if (wrap) text.Append("calc(");
            _expression.AppendTo(text, 0);
            if (wrap) text.Append(')');
            return text.ToString();
        }
        public static implicit operator CSSNumericValue(CSSUnitValue value) => new(value);
    }
}
