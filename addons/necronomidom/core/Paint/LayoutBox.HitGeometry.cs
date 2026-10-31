using NecronomiDom.Dom;
using NecronomiDom.Layout;

namespace NecronomiDom.Paint;

public sealed partial record LayoutBox
{
    internal UsedLayoutGeometry? UsedGeometry { get; init; }
    internal LayoutHitClip? HitClip { get; init; }
    internal LayoutHitTransform? HitTransform { get; init; }
    private Rect? _visualBounds;
    /// <summary>Axis-aligned viewport bounds after the actual ancestor/own CSS affine composition.</summary>
    public Rect VisualBounds { get => _visualBounds ?? Bounds; internal init => _visualBounds = value; }
    /// <summary>Maps native text hit coordinates through the same cached inverse as box hit testing.</summary>
    public bool TryMapPoint(double x, double y, out double localX, out double localY)
    {
        if (HitTransform is { } transform) return transform.Map(x, y, out localX, out localY);
        localX = x; localY = y; return true;
    }
    /// <summary>Tests the actual outer border shape and inherited subtree clips, not a rounded ancestor's visible-overflow content.</summary>
    public bool ContainsPoint(double x, double y)
    {
        if (HitTransform is { } transform)
            return transform.MapQuery(x, y, out var query) && query.Intersects(Bounds)
                && (HitClip == null || HitClip.Contains(x, y));
        return Bounds.Contains(x, y) && (HitClip == null || HitClip.Contains(x, y));
    }
}

internal readonly record struct UsedLayoutGeometry(PositionedPaintContext Context,
    ConvexPolygon? Polygon, PositionedMode Mode, Element? ContainingElement, BorderRadii BorderRadii, UsedBoxTransform Transform);

/// <summary>Persistent links to the same final used geometry emitted to the paint list. No DOM or global geometry registry.</summary>
internal sealed class LayoutHitClip
{
    internal readonly LayoutHitClip? Parent;
    internal readonly Element Owner;
    internal readonly Rect Bounds;
    internal readonly ConvexPolygon? Polygon;
    internal readonly double Radius;
    internal readonly bool Overflow, ClipsX, ClipsY;
    private readonly double _innerLeft, _innerTop, _innerRight, _innerBottom, _radiusSquared;
    private readonly RoundedBorderHitGeometry? _corners;
    private readonly LayoutHitTransform? _transform;
    internal LayoutHitClip(LayoutHitClip? parent, Element owner, Rect bounds, ConvexPolygon? polygon,
        double radius, bool overflow, bool clipsX = true, bool clipsY = true, BorderRadii? corners = null,
        LayoutHitTransform? transform = null)
    {
        Parent = parent; Owner = owner; Bounds = bounds; Polygon = polygon; Radius = radius;
        Overflow = overflow; ClipsX = clipsX; ClipsY = clipsY;
        _transform = transform;
        _corners = corners is { } shape ? new RoundedBorderHitGeometry(bounds, shape)
            : radius > 0 && transform != null
                ? new RoundedBorderHitGeometry(bounds, new BorderRadii(new(radius, radius), new(radius, radius), new(radius, radius), new(radius, radius)))
                : null;
        if (radius > 0 && _corners == null)
        {
            var rounded = new RoundedRectGeometry(bounds, radius);
            _innerLeft = bounds.X + rounded.Radius; _innerTop = bounds.Y + rounded.Radius;
            _innerRight = bounds.X + bounds.Width - rounded.Radius; _innerBottom = bounds.Y + bounds.Height - rounded.Radius;
            _radiusSquared = rounded.Radius * rounded.Radius;
        }
    }
    private LayoutHitClip(LayoutHitClip? parent, LayoutHitClip original)
    {
        Parent = parent; Owner = original.Owner; Bounds = original.Bounds; Polygon = original.Polygon; Radius = original.Radius;
        Overflow = original.Overflow; ClipsX = original.ClipsX; ClipsY = original.ClipsY;
        _innerLeft = original._innerLeft; _innerTop = original._innerTop;
        _innerRight = original._innerRight; _innerBottom = original._innerBottom;
        _radiusSquared = original._radiusSquared; _corners = original._corners;
        _transform = original._transform;
    }
    internal bool Contains(double x, double y)
    {
        for (LayoutHitClip? clip = this; clip != null; clip = clip.Parent)
        {
            double localX = x, localY = y;
            if (clip._transform is { } transform)
            {
                if (!transform.MapQuery(x, y, out var query)) return false;
                if (clip.Polygon != null)
                { if (!query.Intersects(clip.Polygon, clip.Bounds)) return false; }
                else if (clip._corners is { } transformedCorners)
                { if (!transformedCorners.Intersects(query)) return false; }
                else if (!query.Intersects(clip.Bounds, clip.ClipsX, clip.ClipsY)) return false;
                continue;
            }
            if (clip.Polygon != null)
            { if (!clip.Polygon.Contains(localX - clip.Bounds.X, localY - clip.Bounds.Y)) return false; }
            else if (clip._corners is { } corners)
            { if (!corners.ContainsHitPixel(localX, localY)) return false; }
            else if (clip.Radius > 0)
            { if (!clip.ContainsRoundedHitPixel(localX, localY)) return false; }
            else if (clip.ClipsX && (localX < clip.Bounds.X || localX >= clip.Bounds.X + clip.Bounds.Width)
                || clip.ClipsY && (localY < clip.Bounds.Y || localY >= clip.Bounds.Y + clip.Bounds.Height)) return false;
        }
        return true;
    }
    private bool ContainsRoundedHitPixel(double x, double y)
    {
        if (!Bounds.Contains(x, y)) return false;
        // Browser rounded hit tests intersect the one-CSS-pixel query quad, not its mathematical
        // top-left point. Keep painted coverage/geometry unchanged and intersect the actual shape.
        double dx = System.Math.Max(0, System.Math.Max(_innerLeft - (x + 1), x - _innerRight));
        double dy = System.Math.Max(0, System.Math.Max(_innerTop - (y + 1), y - _innerBottom));
        return dx * dx + dy * dy <= _radiusSquared;
    }
    internal LayoutHitClip? ForAbsolute(Element? containingElement)
    {
        LayoutHitClip? parent = Parent?.ForAbsolute(containingElement);
        bool containsBlock = Owner.ParentElement == null;
        for (var ancestor = containingElement; ancestor != null && !containsBlock; ancestor = ancestor.ParentElement)
            containsBlock = ReferenceEquals(ancestor, Owner);
        if (Overflow && !containsBlock) return parent;
        return ReferenceEquals(parent, Parent) ? this : new LayoutHitClip(parent, this);
    }
}
