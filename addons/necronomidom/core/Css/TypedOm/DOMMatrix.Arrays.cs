using System;
using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    public partial struct DOMMatrix
    {
        public static DOMMatrix FromFloat32Array(float[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            return FromFloat32Array(values.AsSpan());
        }

        public static DOMMatrix FromFloat32Array(ReadOnlySpan<float> values)
        {
            if (values.Length == 6)
                return new DOMMatrix { M11 = values[0], M12 = values[1], M21 = values[2],
                    M22 = values[3], M41 = values[4], M42 = values[5] };
            if (values.Length != 16) throw new DomTypeError("A DOMMatrix array must have six or sixteen entries.");
            var result = new DOMMatrix
            {
                M11 = values[0], M12 = values[1], M13 = values[2], M14 = values[3],
                M21 = values[4], M22 = values[5], M23 = values[6], M24 = values[7],
                M31 = values[8], M32 = values[9], M33 = values[10], M34 = values[11],
                M41 = values[12], M42 = values[13], M43 = values[14], M44 = values[15]
            };
            result.Mark3D();
            return result;
        }

        public static DOMMatrix FromFloat64Array(double[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            return FromFloat64Array(values.AsSpan());
        }

        public static DOMMatrix FromFloat64Array(ReadOnlySpan<double> values)
        {
            if (values.Length == 6)
                return new DOMMatrix { M11 = values[0], M12 = values[1], M21 = values[2],
                    M22 = values[3], M41 = values[4], M42 = values[5] };
            if (values.Length != 16) throw new DomTypeError("A DOMMatrix array must have six or sixteen entries.");
            var result = new DOMMatrix
            {
                M11 = values[0], M12 = values[1], M13 = values[2], M14 = values[3],
                M21 = values[4], M22 = values[5], M23 = values[6], M24 = values[7],
                M31 = values[8], M32 = values[9], M33 = values[10], M34 = values[11],
                M41 = values[12], M42 = values[13], M43 = values[14], M44 = values[15]
            };
            result.Mark3D();
            return result;
        }

        public readonly float[] ToFloat32Array() => new[]
        {
            (float)M11, (float)M12, (float)M13, (float)M14,
            (float)M21, (float)M22, (float)M23, (float)M24,
            (float)M31, (float)M32, (float)M33, (float)M34,
            (float)M41, (float)M42, (float)M43, (float)M44
        };

        public readonly double[] ToFloat64Array() => new[]
        {
            M11, M12, M13, M14, M21, M22, M23, M24,
            M31, M32, M33, M34, M41, M42, M43, M44
        };

        /// <summary>The Web IDL default toJSON member set; nonfinite doubles remain numbers.</summary>
        public readonly Dictionary<string, object> ToJson() => new(24, StringComparer.Ordinal)
        {
            ["a"] = A, ["b"] = B, ["c"] = C, ["d"] = D, ["e"] = E, ["f"] = F,
            ["m11"] = M11, ["m12"] = M12, ["m13"] = M13, ["m14"] = M14,
            ["m21"] = M21, ["m22"] = M22, ["m23"] = M23, ["m24"] = M24,
            ["m31"] = M31, ["m32"] = M32, ["m33"] = M33, ["m34"] = M34,
            ["m41"] = M41, ["m42"] = M42, ["m43"] = M43, ["m44"] = M44,
            ["is2D"] = Is2D, ["isIdentity"] = IsIdentity
        };

        internal readonly void EnsureFinite()
        {
            if (!double.IsFinite(M11) || !double.IsFinite(M12) || !double.IsFinite(M13) || !double.IsFinite(M14)
                || !double.IsFinite(M21) || !double.IsFinite(M22) || !double.IsFinite(M23) || !double.IsFinite(M24)
                || !double.IsFinite(M31) || !double.IsFinite(M32) || !double.IsFinite(M33) || !double.IsFinite(M34)
                || !double.IsFinite(M41) || !double.IsFinite(M42) || !double.IsFinite(M43) || !double.IsFinite(M44))
                throw DomException.InvalidState("A nonfinite DOMMatrix cannot be serialized as a CSS transform.");
        }
    }
}
