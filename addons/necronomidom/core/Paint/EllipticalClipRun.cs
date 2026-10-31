using System;

namespace NecronomiDom.Paint;

/// <summary>Mathematical uninflated CSS padding-edge clip, before any ancestor/own affine.
/// ContainsPoint is point geometry, not Chromium's one-CSS-pixel hit-test query quad.</summary>
public readonly record struct EllipticalClipGeometry
{
    public Rect Bounds { get; }
    public BorderRadii Radii { get; }
    public bool IsEmpty => Bounds.Width == 0 || Bounds.Height == 0;

    public EllipticalClipGeometry(Rect bounds, BorderRadii radii)
    {
        PaintGeometryValidation.Bounds(bounds);
        Validate(radii.TopLeft); Validate(radii.TopRight); Validate(radii.BottomRight); Validate(radii.BottomLeft);
        Bounds = bounds; Radii = radii.Normalize(bounds.Width, bounds.Height);
    }

    /// <summary>Resolve outer overlap once on the FINAL border box, then inset actual borders (not CSS padding).</summary>
    public static EllipticalClipGeometry FromBorderBox(Rect borderBounds, BorderSides borders, BorderRadii outerRadii)
    {
        PaintGeometryValidation.Bounds(borderBounds);
        ValidateWidth(borders.Top.Width); ValidateWidth(borders.Right.Width);
        ValidateWidth(borders.Bottom.Width); ValidateWidth(borders.Left.Width);
        Validate(outerRadii.TopLeft); Validate(outerRadii.TopRight); Validate(outerRadii.BottomRight); Validate(outerRadii.BottomLeft);
        var bounds = new Rect(borderBounds.X + borders.Left.Width, borderBounds.Y + borders.Top.Width,
            Math.Max(0, borderBounds.Width - borders.Left.Width - borders.Right.Width),
            Math.Max(0, borderBounds.Height - borders.Top.Width - borders.Bottom.Width));
        return new(bounds, outerRadii.Normalize(borderBounds.Width, borderBounds.Height).Inner(borders));
    }

    public bool ContainsPoint(double x, double y)
    {
        if (IsEmpty || !double.IsFinite(x) || !double.IsFinite(y) || !Bounds.Contains(x, y)) return false;
        double left = x - Bounds.X, top = y - Bounds.Y;
        double right = Bounds.Width - left, bottom = Bounds.Height - top;
        return CornerContains(left, top, Radii.TopLeft) && CornerContains(right, top, Radii.TopRight)
            && CornerContains(right, bottom, Radii.BottomRight) && CornerContains(left, bottom, Radii.BottomLeft);
    }
    private static bool CornerContains(double x, double y, BorderCorner corner)
    {
        // CSS Backgrounds 3: either zero component makes the corner square.
        if (corner.X == 0 || corner.Y == 0 || x >= corner.X || y >= corner.Y) return true;
        double dx = (x - corner.X) / corner.X, dy = (y - corner.Y) / corner.Y;
        return dx * dx + dy * dy <= 1;
    }
    private static void Validate(BorderCorner corner)
    {
        ValidateWidth(corner.X); ValidateWidth(corner.Y);
    }
    private static void ValidateWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width), "Used clip insets/radii must be finite and nonnegative.");
    }
}

public sealed record BeginEllipticalClipRun : PaintRun
{
    public EllipticalClipGeometry Geometry { get; }
    public Rect Bounds => Geometry.Bounds;
    public BorderRadii Radii => Geometry.Radii;
    public BeginEllipticalClipRun(Rect bounds, BorderRadii radii) => Geometry = new(bounds, radii);
}
public sealed record EndEllipticalClipRun(Rect Bounds) : PaintRun;
