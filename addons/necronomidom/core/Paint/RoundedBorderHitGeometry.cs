using System;

namespace NecronomiDom.Paint;

/// <summary>Final normalized CSS border corners intersected by the browser's one-CSS-pixel hit query.
/// Centers and reciprocal squared radii are cached once with layout, never allocated during input.</summary>
internal readonly partial struct RoundedBorderHitGeometry
{
    private readonly Rect _bounds;
    private readonly Corner _topLeft, _topRight, _bottomRight, _bottomLeft;
    private readonly struct Corner
    {
        internal readonly double X, Y, InverseX2, InverseY2;
        internal readonly bool Active;
        internal Corner(double x, double y, BorderCorner radius)
        {
            X = x; Y = y; Active = radius.X > 0 && radius.Y > 0;
            InverseX2 = Active ? 1 / (radius.X * radius.X) : 0;
            InverseY2 = Active ? 1 / (radius.Y * radius.Y) : 0;
        }
        internal bool Intersects(double dx, double dy) => dx * dx * InverseX2 + dy * dy * InverseY2 <= 1;
    }
    internal RoundedBorderHitGeometry(Rect bounds, BorderRadii radii)
    {
        _bounds = bounds;
        _topLeft = new(bounds.X + radii.TopLeft.X, bounds.Y + radii.TopLeft.Y, radii.TopLeft);
        _topRight = new(bounds.X + bounds.Width - radii.TopRight.X, bounds.Y + radii.TopRight.Y, radii.TopRight);
        _bottomRight = new(bounds.X + bounds.Width - radii.BottomRight.X, bounds.Y + bounds.Height - radii.BottomRight.Y, radii.BottomRight);
        _bottomLeft = new(bounds.X + radii.BottomLeft.X, bounds.Y + bounds.Height - radii.BottomLeft.Y, radii.BottomLeft);
    }
    internal bool ContainsHitPixel(double x, double y)
    {
        if (!_bounds.Contains(x, y)) return false;
        if (_topLeft.Active && x < _topLeft.X && y < _topLeft.Y)
            return _topLeft.Intersects(Math.Max(0, _topLeft.X - (x + 1)), Math.Max(0, _topLeft.Y - (y + 1)));
        if (_topRight.Active && x > _topRight.X && y < _topRight.Y)
            return _topRight.Intersects(x - _topRight.X, Math.Max(0, _topRight.Y - (y + 1)));
        if (_bottomRight.Active && x > _bottomRight.X && y > _bottomRight.Y)
            return _bottomRight.Intersects(x - _bottomRight.X, y - _bottomRight.Y);
        if (_bottomLeft.Active && x < _bottomLeft.X && y > _bottomLeft.Y)
            return _bottomLeft.Intersects(Math.Max(0, _bottomLeft.X - (x + 1)), y - _bottomLeft.Y);
        return true;
    }
}
