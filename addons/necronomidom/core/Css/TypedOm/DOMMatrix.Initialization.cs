using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    public partial struct DOMMatrix
    {
        /// <summary>Geometry Interfaces § 6 dictionary fixup: aliases compare with SameValueZero, and is2D is validated before construction.</summary>
        public static DOMMatrix FromMatrix(DOMMatrixInit? values = null)
        {
            if (values == null) return new DOMMatrix();
            double m11 = Alias(values.M11, values.A, 1), m12 = Alias(values.M12, values.B, 0);
            double m21 = Alias(values.M21, values.C, 0), m22 = Alias(values.M22, values.D, 1);
            double m41 = Alias(values.M41, values.E, 0), m42 = Alias(values.M42, values.F, 0);
            double m13 = values.M13 ?? 0, m14 = values.M14 ?? 0, m23 = values.M23 ?? 0, m24 = values.M24 ?? 0;
            double m31 = values.M31 ?? 0, m32 = values.M32 ?? 0, m33 = values.M33 ?? 1, m34 = values.M34 ?? 0;
            double m43 = values.M43 ?? 0, m44 = values.M44 ?? 1;
            bool twoDimensional = m13 == 0 && m14 == 0 && m23 == 0 && m24 == 0 && m31 == 0 && m32 == 0
                && m33 == 1 && m34 == 0 && m43 == 0 && m44 == 1;
            if (values.Is2D == true && !twoDimensional) throw new DomTypeError("The matrix dictionary contradicts is2D.");
            return new DOMMatrix
            {
                _is3D = values.Is2D == false || !twoDimensional,
                M11 = m11, M12 = m12, M13 = m13, M14 = m14,
                M21 = m21, M22 = m22, M23 = m23, M24 = m24,
                M31 = m31, M32 = m32, M33 = m33, M34 = m34,
                M41 = m41, M42 = m42, M43 = m43, M44 = m44,
            };
        }
        private static double Alias(double? member, double? alias, double fallback)
        {
            if (member.HasValue && alias.HasValue && member.Value != alias.Value
                && !(double.IsNaN(member.Value) && double.IsNaN(alias.Value)))
                throw new DomTypeError("The matrix dictionary's alias and matrix member disagree.");
            return member ?? alias ?? fallback;
        }
    }
}
