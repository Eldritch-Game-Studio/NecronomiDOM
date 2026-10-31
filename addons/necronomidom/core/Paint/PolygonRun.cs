using System;
using System.Collections.Generic;

namespace NecronomiDom.Paint;

public readonly record struct PaintPoint(double X, double Y);

/// <summary>Immutable local CSS-pixel vertices. Both windings and empty, collapsed convex shapes are valid.</summary>
public sealed class ConvexPolygon
{
    private readonly PaintPoint[] _points;
    public int Count => _points.Length;
    public PaintPoint this[int index] => _points[index];
    public bool IsEmpty { get; }
    public double Winding { get; }

    public ConvexPolygon(IReadOnlyList<PaintPoint> points) : this(Copy(points)) { }

    // The CSS resolver transfers its freshly allocated array; public callers never transfer mutable storage.
    internal static ConvexPolygon FromOwned(PaintPoint[] points) => new(points);
    private ConvexPolygon(PaintPoint[] points)
    {
        if (points.Length < 3) throw new ArgumentException("A polygon requires at least three vertices.", nameof(points));
        _points = points;
        double area = 0;
        foreach (PaintPoint p in points)
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) throw new ArgumentException("Polygon coordinates must be finite.", nameof(points));
        // Translate before accumulating area, avoiding cancellation for large local coordinates.
        for (int i = 1; i + 1 < Count; i++) area += Cross(_points[0], _points[i], _points[i + 1]);
        if (!double.IsFinite(area)) throw new ArgumentException("Polygon area exceeds the finite coordinate range.", nameof(points));
        Winding = Math.Sign(area);
        IsEmpty = area == 0;
        for (int i = 0; i < Count; i++)
            for (int j = 0; j < Count; j++)
            {
                double cross = Cross(_points[i], _points[(i + 1) % Count], _points[j]);
                if (!double.IsFinite(cross) || (IsEmpty ? cross != 0 : cross * Winding < 0))
                    throw new NotSupportedException("Only convex polygon paint geometry is supported.");
            }
    }

    private static PaintPoint[] Copy(IReadOnlyList<PaintPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var copy = new PaintPoint[points.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = points[i];
        return copy;
    }

    public bool Contains(double x, double y)
    {
        if (IsEmpty || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        var p = new PaintPoint(x, y);
        for (int i = 0; i < Count; i++)
            if (Cross(_points[i], _points[(i + 1) % Count], p) * Winding < 0) return false;
        return true;
    }

    private static double Cross(PaintPoint a, PaintPoint b, PaintPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}

/// <summary>Filled local convex vertices translated by the element's declared document-space reference box.</summary>
public sealed record PolygonRun : PaintRun
{
    public Rect Bounds { get; }
    public ConvexPolygon Geometry { get; }
    public Rgba Color { get; }
    public PolygonRun(Rect bounds, ConvexPolygon geometry, Rgba color)
    {
        PaintGeometryValidation.Bounds(bounds);
        ArgumentNullException.ThrowIfNull(geometry);
        if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B) || !float.IsFinite(color.A))
            throw new ArgumentException("Polygon color must be finite.", nameof(color));
        Bounds = bounds; Geometry = geometry; Color = color;
    }
}

internal static class PaintGeometryValidation
{
    internal static void Bounds(Rect bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
            || bounds.Width < 0 || bounds.Height < 0 || !double.IsFinite(bounds.X + bounds.Width) || !double.IsFinite(bounds.Y + bounds.Height))
            throw new ArgumentException("Paint bounds must be finite with nonnegative dimensions.", nameof(bounds));
    }
}
