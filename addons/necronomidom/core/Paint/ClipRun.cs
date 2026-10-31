using System;

namespace NecronomiDom.Paint;

public sealed record BeginRectClipRun : PaintRun
{
    public Rect Bounds { get; }
    public BeginRectClipRun(Rect bounds) { PaintGeometryValidation.Bounds(bounds); Bounds = bounds; }
}
public sealed record EndRectClipRun : PaintRun;

/// <summary>CSS clip-path masks the complete element subtree, not just its background.</summary>
public sealed record BeginPolygonClipRun : PaintRun
{
    public Rect Bounds { get; }
    public ConvexPolygon Geometry { get; }
    public BeginPolygonClipRun(Rect bounds, ConvexPolygon geometry)
    {
        PaintGeometryValidation.Bounds(bounds); ArgumentNullException.ThrowIfNull(geometry);
        Bounds = bounds; Geometry = geometry;
    }
}
public sealed record EndPolygonClipRun : PaintRun;
