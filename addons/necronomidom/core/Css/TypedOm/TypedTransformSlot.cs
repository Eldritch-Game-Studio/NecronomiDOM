using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>Declaration-owned typed transform and a reusable real function AST for the existing computed resolver.</summary>
    internal sealed class TypedTransformSlot : CssListValue
    {
        private readonly CSSTransformValue _current;
        private readonly CSSTransformComponent[] _components;
        internal TypedTransformSlot(CSSTransformValue value) : this(value, new List<CssValue>(value.Components.Count)) { }
        private TypedTransformSlot(CSSTransformValue value, List<CssValue> functions) : base(functions, CssListSeparator.Space)
        {
            _components = new CSSTransformComponent[value.Components.Count];
            _current = new CSSTransformValue(_components);
            Update(value);
            for (int i = 0; i < value.Components.Count; i++) functions.Add(Function(i, value.Components[i]));
        }
        internal CSSTransformValue Current => _current;
        internal bool SameShape(CSSTransformValue value)
        {
            if (value.Components.Count != Items.Count) return false;
            for (int i = 0; i < Items.Count; i++)
            {
                var old = _components[i]; var current = value.Components[i];
                if (old.PerspectiveNone != current.PerspectiveNone) return false;
                if (Items[i] is not CssFunctionValue function || function.Name != FunctionName(current)) return false;
                if (current.Kind == CSSTransformKind.Matrix) continue;
                int fields = current.Kind switch { CSSTransformKind.Translate or CSSTransformKind.Scale => current.Is2D ? 2 : 3,
                    CSSTransformKind.Rotate => current.Is2D ? 0 : 3, CSSTransformKind.Skew => 2, _ => 1 };
                for (int field = 0; field < fields; field++)
                    if (!SameNumericShape(Numeric(old, field), Numeric(current, field))) return false;
                if (current.Kind == CSSTransformKind.Rotate && !SameNumericShape(old.Angle, current.Angle)) return false;
            }
            return true;
        }
        private static bool SameNumericShape(CSSNumericValue a, CSSNumericValue b)
        {
            bool leftUnit = a.TryGetUnit(out var left), rightUnit = b.TryGetUnit(out var right);
            if (leftUnit != rightUnit) return false;
            if (!leftUnit) return true;
            return (left.Unit == "number" ? 0 : left.Unit == "percent" ? 1 : 2)
                == (right.Unit == "number" ? 0 : right.Unit == "percent" ? 1 : 2);
        }
        internal void Update(CSSTransformValue value)
        { for (int i = 0; i < _components.Length; i++) _components[i] = value.Components[i]; }
        internal CSSTransformValue Snapshot()
        {
            var components = new CSSTransformComponent[_current.Components.Count];
            for (int i = 0; i < components.Length; i++) components[i] = _current.Components[i];
            return new CSSTransformValue(components);
        }
        public override void AppendTo(StringBuilder text) => _current.AppendTo(text);
        private static string FunctionName(CSSTransformComponent value) => value.Kind switch
        {
            CSSTransformKind.Translate => value.Is2D ? "translate" : "translate3d",
            CSSTransformKind.Scale => value.Is2D ? "scale" : "scale3d",
            CSSTransformKind.Rotate => value.Is2D ? "rotate" : "rotate3d",
            CSSTransformKind.Skew => "skew", CSSTransformKind.Perspective => "perspective",
            _ => value.Is2D ? "matrix" : "matrix3d",
        };
        private CssFunctionValue Function(int index, CSSTransformComponent value)
        {
            var arguments = new List<CssValue>();
            switch (value.Kind)
            {
                case CSSTransformKind.Translate:
                    arguments.Add(Projection(index, 0)); arguments.Add(Projection(index, 1));
                    if (!value.Is2D) arguments.Add(Projection(index, 2));
                    break;
                case CSSTransformKind.Scale:
                    arguments.Add(Projection(index, 0)); arguments.Add(Projection(index, 1));
                    if (!value.Is2D) arguments.Add(Projection(index, 2));
                    break;
                case CSSTransformKind.Rotate:
                    if (!value.Is2D) for (int axis = 0; axis < 3; axis++) arguments.Add(Projection(index, axis));
                    arguments.Add(Projection(index, 3)); break;
                case CSSTransformKind.Skew:
                    arguments.Add(Projection(index, 0)); arguments.Add(Projection(index, 1)); break;
                case CSSTransformKind.Perspective:
                    arguments.Add(value.PerspectiveNone ? new CssKeywordValue("none") : Projection(index, 0)); break;
                case CSSTransformKind.Matrix:
                    if (value.Is2D)
                    {
                        foreach (int field in new[] { 0, 1, 4, 5, 12, 13 }) arguments.Add(new NumberProjection(this, index, field + 4));
                    }
                    else for (int field = 0; field < 16; field++) arguments.Add(new NumberProjection(this, index, field + 4));
                    break;
            }
            return new CssFunctionValue(FunctionName(value), new CssListValue(arguments, CssListSeparator.Comma));
        }
        private CssValue Projection(int index, int field)
        {
            if (!Numeric(_components[index], field).TryGetUnit(out var unit)) return new MathProjection(this, index, field);
            return unit.Unit == "number" ? new NumberProjection(this, index, field)
                : unit.Unit == "percent" ? new PercentageProjection(this, index, field) : new DimensionProjection(this, index, field);
        }
        private static CSSNumericValue Numeric(CSSTransformComponent component, int field)
            => field switch { 0 => component.X, 1 => component.Y, 2 => component.Z, _ => component.Angle };
        private CSSUnitValue Unit(int index, int field)
        {
            Numeric(_components[index], field).TryGetUnit(out var unit);
            return unit;
        }
        private double Number(int index, int field)
        {
            if (field < 4) return Unit(index, field).Value;
            DOMMatrix matrix = _current.Components[index].Matrix;
            return (field - 4) switch
            {
                0 => matrix.M11, 1 => matrix.M12, 2 => matrix.M13, 3 => matrix.M14,
                4 => matrix.M21, 5 => matrix.M22, 6 => matrix.M23, 7 => matrix.M24,
                8 => matrix.M31, 9 => matrix.M32, 10 => matrix.M33, 11 => matrix.M34,
                12 => matrix.M41, 13 => matrix.M42, 14 => matrix.M43, _ => matrix.M44,
            };
        }
        private sealed class NumberProjection : CssNumberValue
        {
            private readonly TypedTransformSlot _owner; private readonly int _index, _field;
            internal NumberProjection(TypedTransformSlot owner, int index, int field) : base(0, false) { _owner = owner; _index = index; _field = field; }
            public override double Value => _owner.Number(_index, _field);
            public override bool IsInteger => Value == System.Math.Truncate(Value);
        }
        private sealed class DimensionProjection : CssDimensionValue
        {
            private readonly TypedTransformSlot _owner; private readonly int _index, _field;
            internal DimensionProjection(TypedTransformSlot owner, int index, int field) : base(0, "px") { _owner = owner; _index = index; _field = field; }
            public override double Value => _owner.Unit(_index, _field).Value;
            public override string Unit => _owner.Unit(_index, _field).NumericUnit;
        }
        private sealed class PercentageProjection : CssPercentageValue
        {
            private readonly TypedTransformSlot _owner; private readonly int _index, _field;
            internal PercentageProjection(TypedTransformSlot owner, int index, int field) : base(0) { _owner = owner; _index = index; _field = field; }
            public override double Value => _owner.Unit(_index, _field).Value;
        }
        private sealed class MathProjection : CssMathValue
        {
            private readonly TypedTransformSlot _owner; private readonly int _index, _field;
            internal MathProjection(TypedTransformSlot owner, int index, int field)
                : base(Numeric(owner._components[index], field).Expression, Numeric(owner._components[index], field).Type)
            { _owner = owner; _index = index; _field = field; }
            public override CssMathExpression Root => Numeric(_owner._components[_index], _field).Expression;
            public override CssNumericType Type => Numeric(_owner._components[_index], _field).Type;
        }
    }
}
