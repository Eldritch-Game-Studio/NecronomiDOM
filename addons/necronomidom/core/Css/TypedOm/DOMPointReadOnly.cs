using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>A value-backed read-only snapshot, suitable for geometry API inputs.</summary>
    public readonly struct DOMPointReadOnly : IEquatable<DOMPointReadOnly>
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double W { get; }

        public DOMPointReadOnly() : this(0) { }
        public DOMPointReadOnly(double x, double y = 0, double z = 0, double w = 1)
        { X = x; Y = y; Z = z; W = w; }

        public static DOMPointReadOnly FromPoint(DOMPointReadOnly? other = null)
            => other ?? new DOMPointReadOnly();

        public DOMPoint MatrixTransform(DOMMatrix? matrix = null)
            => (matrix ?? DOMMatrix.Identity).TransformPoint(this);

        public Dictionary<string, double> ToJson() => new(4, StringComparer.Ordinal)
        { ["x"] = X, ["y"] = Y, ["z"] = Z, ["w"] = W };

        public bool Equals(DOMPointReadOnly other)
            => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        public override bool Equals(object? obj) => obj is DOMPointReadOnly point && Equals(point);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    }
}
