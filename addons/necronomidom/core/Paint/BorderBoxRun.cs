using System;

namespace NecronomiDom.Paint;

public readonly record struct BorderSide(double Width, Rgba Color, string Style);
public readonly record struct BorderSides(BorderSide Top, BorderSide Right, BorderSide Bottom, BorderSide Left);
public readonly record struct BorderCorner(double X, double Y);
public readonly record struct BorderRadii(BorderCorner TopLeft, BorderCorner TopRight, BorderCorner BottomRight, BorderCorner BottomLeft)
{
    /// <summary>CSS Backgrounds 3 corner overlap reduction uses one common factor for all eight radii.</summary>
    public BorderRadii Normalize(double width, double height)
    {
        double factor = 1;
        Reduce(width, TopLeft.X + TopRight.X, ref factor);
        Reduce(width, BottomLeft.X + BottomRight.X, ref factor);
        Reduce(height, TopLeft.Y + BottomLeft.Y, ref factor);
        Reduce(height, TopRight.Y + BottomRight.Y, ref factor);
        return new(Scale(TopLeft, factor), Scale(TopRight, factor), Scale(BottomRight, factor), Scale(BottomLeft, factor));
    }
    private static void Reduce(double size, double sum, ref double factor)
    { if (sum > 0) factor = Math.Min(factor, Math.Max(0, size) / sum); }
    private static BorderCorner Scale(BorderCorner corner, double factor) => new(corner.X * factor, corner.Y * factor);
    public BorderRadii Inner(BorderSides sides) => new(
        Inset(TopLeft, sides.Left.Width, sides.Top.Width), Inset(TopRight, sides.Right.Width, sides.Top.Width),
        Inset(BottomRight, sides.Right.Width, sides.Bottom.Width), Inset(BottomLeft, sides.Left.Width, sides.Bottom.Width));
    private static BorderCorner Inset(BorderCorner corner, double x, double y) => new(Math.Max(0, corner.X - x), Math.Max(0, corner.Y - y));
}

public sealed record BorderBoxRun(Rect Bounds, Rgba Background, BorderSides Borders, BorderRadii Radii) : PaintRun;
