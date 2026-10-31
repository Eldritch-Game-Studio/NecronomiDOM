using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM 1 § 5 transform component kind.</summary>
    public enum CSSTransformKind { Matrix, Translate, Rotate, Scale, Skew, Perspective }
    /// <summary>Struct-backed CSS transform component; percentages remain unresolved until layout supplies a basis.</summary>
    public readonly record struct CSSTransformComponent(CSSTransformKind Kind, CSSNumericValue X, CSSNumericValue Y,
        CSSNumericValue Z, CSSNumericValue Angle, DOMMatrix Matrix, bool Is2D)
    {
        public bool PerspectiveNone { get; init; }
        public static CSSTransformComponent Translate(CSSNumericValue x, CSSNumericValue y, CSSNumericValue? z = null)
            => Checked(new(CSSTransformKind.Translate, x, y, z ?? new CSSUnitValue(0, "px"), default, default, z == null));
        public static CSSTransformComponent Scale(double x, double y, double? z = null)
            => Scale(new CSSUnitValue(x, "number"), new CSSUnitValue(y, "number"), z.HasValue ? new CSSNumericValue(new CSSUnitValue(z.Value, "number")) : null);
        public static CSSTransformComponent Scale(CSSNumericValue x, CSSNumericValue y, CSSNumericValue? z = null)
            => Checked(new(CSSTransformKind.Scale, x, y, z ?? new CSSUnitValue(1, "number"), default, default, z == null));
        public static CSSTransformComponent Rotate(CSSNumericValue angle, double x = 0, double y = 0, double z = 1)
            => Checked(new(CSSTransformKind.Rotate, new CSSUnitValue(x, "number"), new CSSUnitValue(y, "number"), new CSSUnitValue(z, "number"), angle, default, x == 0 && y == 0 && z == 1));
        public static CSSTransformComponent Rotate(CSSNumericValue x, CSSNumericValue y, CSSNumericValue z, CSSNumericValue angle)
            => Checked(new(CSSTransformKind.Rotate, x, y, z, angle, default, false));
        public static CSSTransformComponent Skew(CSSNumericValue x, CSSNumericValue y)
            => Checked(new(CSSTransformKind.Skew, x, y, default, default, default, true));
        public static CSSTransformComponent Perspective(CSSNumericValue length)
            => Checked(new(CSSTransformKind.Perspective, length, default, default, default, default, false));
        public static CSSTransformComponent Perspective(CSSKeywordValue value)
        {
            if (value.Value != "none") throw new DomTypeError("CSSPerspective accepts only the none keyword.");
            return new(CSSTransformKind.Perspective, default, default, default, default, default, false) { PerspectiveNone = true };
        }
        public static CSSTransformComponent Perspective(string value) => Perspective(new CSSKeywordValue(value));
        public static CSSTransformComponent FromMatrix(DOMMatrix matrix, bool is2D = false)
            => new(CSSTransformKind.Matrix, default, default, default, default, matrix, is2D);
        private static CSSTransformComponent Checked(CSSTransformComponent component) { component.Validate(); return component; }
        internal void Validate()
        {
            bool valid = Kind switch
            {
                CSSTransformKind.Translate => Fits(X, CssNumericDimension.Length, true) && Fits(Y, CssNumericDimension.Length, true) && Fits(Z, CssNumericDimension.Length),
                CSSTransformKind.Scale => Fits(X, CssNumericDimension.Number) && Fits(Y, CssNumericDimension.Number) && Fits(Z, CssNumericDimension.Number),
                CSSTransformKind.Rotate => Fits(X, CssNumericDimension.Number) && Fits(Y, CssNumericDimension.Number) && Fits(Z, CssNumericDimension.Number) && Fits(Angle, CssNumericDimension.Angle),
                CSSTransformKind.Skew => Fits(X, CssNumericDimension.Angle) && Fits(Y, CssNumericDimension.Angle),
                CSSTransformKind.Perspective => PerspectiveNone || Fits(X, CssNumericDimension.Length) && (!X.TryGetUnit(out var length) || length.Value >= 0),
                CSSTransformKind.Matrix => true,
                _ => false,
            };
            if (!valid) throw new DomTypeError("CSS transform component dimensions are invalid.");
        }
        private static bool Fits(CSSNumericValue value, CssNumericDimension dimension, bool percentage = false)
            => value.IsInitialized && value.Type.Matches(dimension, percentage);
        public DOMMatrix ToMatrix()
        {
            switch (Kind)
            {
                case CSSTransformKind.Matrix:
                    if (!Is2D) { var matrix = Matrix; matrix.Mark3D(); return matrix; }
                    return new DOMMatrix { M11 = Matrix.M11, M12 = Matrix.M12, M21 = Matrix.M21, M22 = Matrix.M22, M41 = Matrix.M41, M42 = Matrix.M42 };
                case CSSTransformKind.Translate: return DOMMatrix.CreateTranslation(X.To("px").Value, Y.To("px").Value, Is2D ? 0 : Z.To("px").Value);
                case CSSTransformKind.Scale: return DOMMatrix.CreateScale(X.To("number").Value, Y.To("number").Value, Is2D ? 1 : Z.To("number").Value);
                case CSSTransformKind.Rotate: return DOMMatrix.CreateRotation(Is2D ? 0 : X.To("number").Value, Is2D ? 0 : Y.To("number").Value, Is2D ? 1 : Z.To("number").Value, Angle.To("rad").Value);
                case CSSTransformKind.Skew:
                    var skew = DOMMatrix.Identity;
                    skew.M21 = System.Math.Tan(X.To("rad").Value);
                    skew.M12 = System.Math.Tan(Y.To("rad").Value);
                    return skew;
                case CSSTransformKind.Perspective:
                    if (PerspectiveNone) return DOMMatrix.Identity;
                    double distance = X.To("px").Value;
                    if (distance < 0) throw new DomTypeError("Perspective length must be non-negative.");
                    var perspective = DOMMatrix.Identity;
                    perspective.M34 = -1 / System.Math.Max(distance, 1);
                    return perspective;
                default: throw new DomTypeError("Unknown CSS transform component.");
            }
        }
        internal void AppendTo(StringBuilder text)
        {
            switch (Kind)
            {
                case CSSTransformKind.Translate:
                    text.Append(Is2D ? "translate(" : "translate3d(").Append(X).Append(", ").Append(Y);
                    if (!Is2D) text.Append(", ").Append(Z);
                    text.Append(')'); break;
                case CSSTransformKind.Scale:
                    text.Append(Is2D ? "scale(" : "scale3d(").Append(X).Append(", ").Append(Y);
                    if (!Is2D) text.Append(", ").Append(Z);
                    text.Append(')'); break;
                case CSSTransformKind.Rotate:
                    if (Is2D) text.Append("rotate(").Append(Angle).Append(')');
                    else text.Append("rotate3d(").Append(X).Append(", ").Append(Y).Append(", ").Append(Z).Append(", ").Append(Angle).Append(')');
                    break;
                case CSSTransformKind.Skew: text.Append("skew(").Append(X).Append(", ").Append(Y).Append(')'); break;
                case CSSTransformKind.Perspective: text.Append("perspective(").Append(PerspectiveNone ? "none" : X.ToString()).Append(')'); break;
                case CSSTransformKind.Matrix:
                    if (Is2D) text.Append("matrix(").Append(CssSerialization.SerializeNumber(Matrix.M11)).Append(", ").Append(CssSerialization.SerializeNumber(Matrix.M12))
                        .Append(", ").Append(CssSerialization.SerializeNumber(Matrix.M21)).Append(", ").Append(CssSerialization.SerializeNumber(Matrix.M22))
                        .Append(", ").Append(CssSerialization.SerializeNumber(Matrix.M41)).Append(", ").Append(CssSerialization.SerializeNumber(Matrix.M42)).Append(')');
                    else
                    {
                        text.Append("matrix3d(");
                        AppendMatrix(text, Matrix);
                        text.Append(')');
                    }
                    break;
            }
        }
        private static void AppendMatrix(StringBuilder text, DOMMatrix matrix)
        {
            Span<double> values = stackalloc double[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44 };
            for (int i = 0; i < values.Length; i++) { if (i > 0) text.Append(", "); text.Append(CssSerialization.SerializeNumber(values[i])); }
        }
    }
    /// <summary>CSS Typed OM 1 § 5.1 transform list, preserving the caller's reusable component storage.</summary>
    public readonly struct CSSTransformValue
    {
        private readonly IReadOnlyList<CSSTransformComponent> _components;
        public CSSTransformValue(IReadOnlyList<CSSTransformComponent> components)
        {
            _components = components ?? throw new ArgumentNullException(nameof(components));
            if (components.Count == 0) throw new DomTypeError("A CSS transform list must have a component.");
        }
        public IReadOnlyList<CSSTransformComponent> Components => _components ?? Array.Empty<CSSTransformComponent>();
        public bool Is2D { get { for (int i = 0; i < Components.Count; i++) if (!Components[i].Is2D) return false; return true; } }
        public DOMMatrix ToMatrix()
        {
            if (Components.Count == 0) throw new DomTypeError("An uninitialized transform list cannot be converted to a matrix.");
            var result = DOMMatrix.Identity;
            for (int i = 0; i < Components.Count; i++) result = Components[i].ToMatrix() * result;
            return result;
        }
        public override string ToString() { var text = new StringBuilder(); AppendTo(text); return text.ToString(); }
        internal void AppendTo(StringBuilder text)
        {
            for (int i = 0; i < Components.Count; i++) { if (i > 0) text.Append(' '); Components[i].AppendTo(text); }
        }
    }
}
