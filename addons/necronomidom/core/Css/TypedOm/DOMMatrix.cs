using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>Geometry Interfaces 1 matrix, double precision and value-backed; CSS column order uses transposed row-vector storage.</summary>
    public partial struct DOMMatrix
    {
        internal bool _is3D;
        private double _m13, _m14, _m23, _m24, _m31, _m32, _m33, _m34, _m43, _m44;
        internal void Mark3D() => _is3D = true;
        public double M11 { get; set; } public double M12 { get; set; }
        public double M13 { readonly get => _m13; set { _m13 = value; _is3D |= value != 0; } }
        public double M14 { readonly get => _m14; set { _m14 = value; _is3D |= value != 0; } }
        public double M21 { get; set; } public double M22 { get; set; }
        public double M23 { readonly get => _m23; set { _m23 = value; _is3D |= value != 0; } }
        public double M24 { readonly get => _m24; set { _m24 = value; _is3D |= value != 0; } }
        public double M31 { readonly get => _m31; set { _m31 = value; _is3D |= value != 0; } }
        public double M32 { readonly get => _m32; set { _m32 = value; _is3D |= value != 0; } }
        public double M33 { readonly get => _m33; set { _m33 = value; _is3D |= value != 1; } }
        public double M34 { readonly get => _m34; set { _m34 = value; _is3D |= value != 0; } }
        public double M41 { get; set; } public double M42 { get; set; }
        public double M43 { readonly get => _m43; set { _m43 = value; _is3D |= value != 0; } }
        public double M44 { readonly get => _m44; set { _m44 = value; _is3D |= value != 1; } }
        public double A { readonly get => M11; set => M11 = value; } public double B { readonly get => M12; set => M12 = value; }
        public double C { readonly get => M21; set => M21 = value; } public double D { readonly get => M22; set => M22 = value; }
        public double E { readonly get => M41; set => M41 = value; } public double F { readonly get => M42; set => M42 = value; }
        public DOMMatrix() { M11 = M22 = M33 = M44 = 1; }
        public DOMMatrix(IReadOnlyList<double> values) : this()
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 6) { M11 = values[0]; M12 = values[1]; M21 = values[2]; M22 = values[3]; M41 = values[4]; M42 = values[5]; }
            else if (values.Count == 16)
            {
                _is3D = true;
                M11 = values[0]; M12 = values[1]; M13 = values[2]; M14 = values[3]; M21 = values[4]; M22 = values[5]; M23 = values[6]; M24 = values[7];
                M31 = values[8]; M32 = values[9]; M33 = values[10]; M34 = values[11]; M41 = values[12]; M42 = values[13]; M43 = values[14]; M44 = values[15];
            }
            else throw new DomTypeError("A DOMMatrix sequence must have six or sixteen entries.");
        }
        public static DOMMatrix Identity => new();
        public readonly bool Is2D => !_is3D;
        public readonly bool IsIdentity => M11 == 1 && M12 == 0 && M13 == 0 && M14 == 0
            && M21 == 0 && M22 == 1 && M23 == 0 && M24 == 0 && M31 == 0 && M32 == 0 && M33 == 1 && M34 == 0
            && M41 == 0 && M42 == 0 && M43 == 0 && M44 == 1;
        public static DOMMatrix CreateTranslation(double x, double y, double z) => new() { M41 = x, M42 = y, M43 = z };
        public static DOMMatrix CreateScale(double x, double y, double z) => new() { M11 = x, M22 = y, M33 = z };
        public static DOMMatrix CreateRotation(double x, double y, double z, double radians)
        {
            double length = System.Math.Sqrt(x * x + y * y + z * z);
            if (length == 0) return Identity;
            x /= length; y /= length; z /= length;
            double c = System.Math.Cos(radians), s = System.Math.Sin(radians), t = 1 - c;
            if (x == 0 && y == 0) return new DOMMatrix { M11 = c, M12 = s * z, M21 = -s * z, M22 = c };
            return new DOMMatrix { _is3D = x != 0 || y != 0, M11 = t * x * x + c, M12 = t * x * y + s * z, M13 = t * x * z - s * y,
                M21 = t * x * y - s * z, M22 = t * y * y + c, M23 = t * y * z + s * x,
                M31 = t * x * z + s * y, M32 = t * y * z - s * x, M33 = t * z * z + c };
        }
        public readonly DOMMatrix Multiply(DOMMatrix other) => other * this;
        public DOMMatrix MultiplySelf(DOMMatrix other) { this = Multiply(other); return this; }
        public DOMMatrix PreMultiplySelf(DOMMatrix other) { this *= other; return this; }
        public readonly DOMMatrix Translate(double x = 0, double y = 0, double z = 0) => CreateTranslation(x, y, z) * this;
        public DOMMatrix TranslateSelf(double x = 0, double y = 0, double z = 0) { this = Translate(x, y, z); return this; }
        public readonly DOMMatrix Scale(double x = 1, double? y = null, double z = 1, double originX = 0, double originY = 0, double originZ = 0)
            => ScaleWithOrigin(x, y, z, originX, originY, originZ);
        public DOMMatrix ScaleSelf(double x = 1, double? y = null, double z = 1, double originX = 0, double originY = 0, double originZ = 0)
        { this = Scale(x, y, z, originX, originY, originZ); return this; }
        public readonly DOMMatrix RotateAxisAngle(double x, double y, double z, double degrees) => CreateRotation(x, y, z, degrees * System.Math.PI / 180) * this;
        public DOMMatrix RotateAxisAngleSelf(double x, double y, double z, double degrees) { this = RotateAxisAngle(x, y, z, degrees); return this; }
        public static DOMMatrix operator *(DOMMatrix a, DOMMatrix b) => new()
        {
            _is3D = a._is3D || b._is3D,
            M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41,
            M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42,
            M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43,
            M14 = a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,
            M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41,
            M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42,
            M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43,
            M24 = a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,
            M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41,
            M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42,
            M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43,
            M34 = a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,
            M41 = a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41,
            M42 = a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42,
            M43 = a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43,
            M44 = a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44,
        };
        public override readonly string ToString()
        {
            EnsureFinite();
            var result = new StringBuilder();
            if (Is2D) result.Append("matrix(").Append(SerializeGeometryNumber(M11)).Append(", ").Append(SerializeGeometryNumber(M12))
                .Append(", ").Append(SerializeGeometryNumber(M21)).Append(", ").Append(SerializeGeometryNumber(M22))
                .Append(", ").Append(SerializeGeometryNumber(M41)).Append(", ").Append(SerializeGeometryNumber(M42)).Append(')');
            else
            {
                Span<double> values = stackalloc double[] { M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44 };
                result.Append("matrix3d(");
                for (int i = 0; i < values.Length; i++) { if (i > 0) result.Append(", "); result.Append(SerializeGeometryNumber(values[i])); }
                result.Append(')');
            }
            return result.ToString();
        }
    }
}
