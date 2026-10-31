using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>A double-precision homogeneous point; W is not a normalization request.</summary>
    public struct DOMPoint : IEquatable<DOMPoint>
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double W { get; set; }

        public DOMPoint() : this(0) { }
        public DOMPoint(double x, double y = 0, double z = 0, double w = 1)
        { X = x; Y = y; Z = z; W = w; }

        public static DOMPoint FromPoint(DOMPointReadOnly? other = null)
        {
            var point = other ?? new DOMPointReadOnly();
            return new DOMPoint(point.X, point.Y, point.Z, point.W);
        }

        public readonly DOMPoint MatrixTransform(DOMMatrix? matrix = null)
            => (matrix ?? DOMMatrix.Identity).TransformPoint(this);

        public readonly Dictionary<string, double> ToJson() => new(4, StringComparer.Ordinal)
        { ["x"] = X, ["y"] = Y, ["z"] = Z, ["w"] = W };

        public readonly bool Equals(DOMPoint other)
            => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        public override readonly bool Equals(object? obj) => obj is DOMPoint point && Equals(point);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        public static implicit operator DOMPointReadOnly(DOMPoint point)
            => new(point.X, point.Y, point.Z, point.W);
    }
}
