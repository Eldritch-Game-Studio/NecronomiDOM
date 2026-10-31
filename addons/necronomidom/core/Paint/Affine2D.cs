using System;

namespace NecronomiDom.Paint;

/// <summary>CSS column-vector affine matrix in viewport CSS pixels. Compose applies own, then ancestor.</summary>
public readonly record struct Affine2D(double A, double B, double C, double D, double E, double F)
{
    public static Affine2D Identity => new(1, 0, 0, 1, 0, 0);
    public (double X, double Y) TransformPoint(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
    public static Affine2D Compose(Affine2D ancestor, Affine2D own) => new(
        ancestor.A * own.A + ancestor.C * own.B, ancestor.B * own.A + ancestor.D * own.B,
        ancestor.A * own.C + ancestor.C * own.D, ancestor.B * own.C + ancestor.D * own.D,
        ancestor.A * own.E + ancestor.C * own.F + ancestor.E, ancestor.B * own.E + ancestor.D * own.F + ancestor.F);
    public Rect TransformBounds(Rect bounds)
    {
        var p0 = TransformPoint(bounds.X, bounds.Y);
        var p1 = TransformPoint(bounds.X + bounds.Width, bounds.Y);
        var p2 = TransformPoint(bounds.X, bounds.Y + bounds.Height);
        var p3 = TransformPoint(bounds.X + bounds.Width, bounds.Y + bounds.Height);
        double left = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double top = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        return new(left, top, Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X)) - left,
            Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y)) - top);
    }
    public bool TryInversePoint(double x, double y, out double localX, out double localY)
    {
        double determinant = A * D - B * C;
        localX = localY = double.NaN;
        if (determinant == 0 || !double.IsFinite(determinant)) return false;
        x -= E; y -= F;
        localX = (D * x - C * y) / determinant;
        localY = (A * y - B * x) / determinant;
        return double.IsFinite(localX) && double.IsFinite(localY);
    }
}

public readonly record struct UsedBoxTransform(Affine2D Matrix, bool EstablishesContainingBlock);
public sealed record BeginTransformRun(Rect Bounds, Affine2D Transform) : PaintRun;
public sealed record EndTransformRun(Rect Bounds) : PaintRun;
