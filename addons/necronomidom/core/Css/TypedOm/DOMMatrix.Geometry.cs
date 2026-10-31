using System;

namespace NecronomiDom.Css.TypedOm
{
    public partial struct DOMMatrix
    {
        /// <summary>Applies the homogeneous matrix without dividing by the resulting W.</summary>
        public readonly DOMPoint TransformPoint(DOMPointReadOnly? point = null)
        {
            var p = point ?? new DOMPointReadOnly();
            return new DOMPoint(
                p.X * M11 + p.Y * M21 + p.Z * M31 + p.W * M41,
                p.X * M12 + p.Y * M22 + p.Z * M32 + p.W * M42,
                p.X * M13 + p.Y * M23 + p.Z * M33 + p.W * M43,
                p.X * M14 + p.Y * M24 + p.Z * M34 + p.W * M44);
        }

        public readonly DOMMatrix FlipX() => CreateScale(-1, 1, 1) * this;
        public readonly DOMMatrix FlipY() => CreateScale(1, -1, 1) * this;

        public readonly DOMMatrix Rotate(double rotX = 0, double? rotY = null, double? rotZ = null)
        {
            var result = this;
            return result.RotateSelf(rotX, rotY, rotZ);
        }

        public DOMMatrix RotateSelf(double rotX = 0, double? rotY = null, double? rotZ = null)
        {
            if (!rotY.HasValue && !rotZ.HasValue) { rotZ = rotX; rotX = 0; rotY = 0; }
            double y = rotY ?? 0, z = rotZ ?? 0;
            // CSS post-multiplication is pre-multiplication in our transposed storage.
            // Geometry Interfaces specifies Z, then Y, then X, not Euler XYZ order.
            if (z != 0) this = RotateAxisAngle(0, 0, 1, z);
            if (y != 0) this = RotateAxisAngle(0, 1, 0, y);
            if (rotX != 0) this = RotateAxisAngle(1, 0, 0, rotX);
            if (rotX != 0 || y != 0) _is3D = true;
            return this;
        }

        public readonly DOMMatrix RotateFromVector(double x = 0, double y = 0)
            => Rotate(x == 0 && y == 0 ? 0 : Math.Atan2(y, x) * 180 / Math.PI);

        public DOMMatrix RotateFromVectorSelf(double x = 0, double y = 0)
        { this = RotateFromVector(x, y); return this; }

        public readonly DOMMatrix SkewX(double degrees = 0)
            => new DOMMatrix { M21 = Math.Tan(degrees * Math.PI / 180) } * this;

        public readonly DOMMatrix SkewY(double degrees = 0)
            => new DOMMatrix { M12 = Math.Tan(degrees * Math.PI / 180) } * this;

        public DOMMatrix SkewXSelf(double degrees = 0) { this = SkewX(degrees); return this; }
        public DOMMatrix SkewYSelf(double degrees = 0) { this = SkewY(degrees); return this; }

        public readonly DOMMatrix ScaleNonUniform(double scaleX = 1, double scaleY = 1)
            => Scale(scaleX, scaleY);

        public readonly DOMMatrix Scale3d(double scale = 1, double originX = 0, double originY = 0, double originZ = 0)
            => ScaleWithOrigin(scale, scale, scale, originX, originY, originZ);

        public DOMMatrix Scale3dSelf(double scale = 1, double originX = 0, double originY = 0, double originZ = 0)
        { this = Scale3d(scale, originX, originY, originZ); return this; }

        internal readonly DOMMatrix ScaleWithOrigin(double x, double? y, double z,
            double originX, double originY, double originZ)
        {
            var result = this;
            if (originX != 0 || originY != 0 || originZ != 0)
                result = result.Translate(originX, originY, originZ);
            result = CreateScale(x, y ?? x, z) * result;
            if (originX != 0 || originY != 0 || originZ != 0)
                result = result.Translate(-originX, -originY, -originZ);
            // A nonzero Z origin is observable even if the translations cancel.
            if (z != 1 || originZ != 0) result._is3D = true;
            return result;
        }
    }
}
