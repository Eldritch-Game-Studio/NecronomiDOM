using System;

namespace NecronomiDom.Paint;

/// <summary>Isolate descendants, composite once, then apply opacity to the resulting group.</summary>
public sealed record BeginOpacityRun : PaintRun
{
    public Rect Bounds { get; }
    public float Opacity { get; }
    public BeginOpacityRun(Rect bounds, float opacity)
    {
        PaintGeometryValidation.Bounds(bounds);
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        Bounds = bounds; Opacity = opacity;
    }
}
public sealed record EndOpacityRun : PaintRun;
