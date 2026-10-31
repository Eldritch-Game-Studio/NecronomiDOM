using System;

namespace NecronomiDom.Paint;

/// <summary>One cached inverse per used box/context; pointer sampling never recomputes the determinant.</summary>
internal readonly struct LayoutHitTransform
{
    private readonly Affine2D _inverse;
    private readonly bool _invertible;
    private LayoutHitTransform(Affine2D inverse, bool invertible) { _inverse = inverse; _invertible = invertible; }
    internal static LayoutHitTransform From(Affine2D matrix)
    {
        double determinant = matrix.A * matrix.D - matrix.B * matrix.C;
        if (determinant == 0 || !double.IsFinite(determinant)) return new(default, false);
        double reciprocal = 1 / determinant;
        double a = matrix.D * reciprocal, b = -matrix.B * reciprocal;
        double c = -matrix.C * reciprocal, d = matrix.A * reciprocal;
        var inverse = new Affine2D(a, b, c, d, -a * matrix.E - c * matrix.F, -b * matrix.E - d * matrix.F);
        return new(inverse, double.IsFinite(inverse.A) && double.IsFinite(inverse.B) && double.IsFinite(inverse.C)
            && double.IsFinite(inverse.D) && double.IsFinite(inverse.E) && double.IsFinite(inverse.F));
    }
    internal bool Map(double x, double y, out double localX, out double localY)
    {
        (localX, localY) = _inverse.TransformPoint(x, y);
        return _invertible && double.IsFinite(localX) && double.IsFinite(localY);
    }
    internal bool MapQuery(double x, double y, out HitQueryQuad query)
    {
        bool valid = Map(x, y, out double localX, out double localY);
        query = new(new PaintPoint(localX, localY), new PaintPoint(_inverse.A, _inverse.B), new PaintPoint(_inverse.C, _inverse.D));
        return valid && double.IsFinite(query.P1.X) && double.IsFinite(query.P1.Y)
            && double.IsFinite(query.P2.X) && double.IsFinite(query.P2.Y) && double.IsFinite(query.P3.X) && double.IsFinite(query.P3.Y);
    }
}
