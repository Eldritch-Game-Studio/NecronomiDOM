using System;

namespace NecronomiDom.Paint;

/// <summary>Used padding-edge geometry shared by native masks and document hit routing.</summary>
public readonly record struct RoundedRectGeometry
{
    public Rect Bounds { get; }
    public double Radius { get; }
    public RoundedRectGeometry(Rect bounds, double radius)
    {
        PaintGeometryValidation.Bounds(bounds);
        if (!double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        Bounds = bounds; Radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
    }
    public bool Contains(double x, double y)
    {
        if (!Bounds.Contains(x, y)) return false;
        double nearestX = Math.Clamp(x, Bounds.X + Radius, Bounds.X + Bounds.Width - Radius);
        double nearestY = Math.Clamp(y, Bounds.Y + Radius, Bounds.Y + Bounds.Height - Radius);
        double dx = x - nearestX, dy = y - nearestY;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

public sealed record BeginRoundedRectClipRun : PaintRun
{
    public RoundedRectGeometry Geometry { get; }
    public Rect Bounds => Geometry.Bounds;
    public double Radius => Geometry.Radius;
    public BeginRoundedRectClipRun(Rect bounds, double radius) => Geometry = new RoundedRectGeometry(bounds, radius);
}
public sealed record EndRoundedRectClipRun : PaintRun;
