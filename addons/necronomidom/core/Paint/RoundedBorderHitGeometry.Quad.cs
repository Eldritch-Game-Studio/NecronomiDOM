using System;

namespace NecronomiDom.Paint;

internal readonly partial struct RoundedBorderHitGeometry
{
    internal bool Intersects(in HitQueryQuad quad)
    {
        if (!quad.Intersects(_bounds)) return false;
        for (int i = 0; i < 4; i++)
            if (ContainsShape(quad[i])) return true;
        double right = _bounds.X + _bounds.Width, bottom = _bounds.Y + _bounds.Height;
        var topLeftTop = new PaintPoint(_topLeft.Active ? _topLeft.X : _bounds.X, _bounds.Y);
        var topRightTop = new PaintPoint(_topRight.Active ? _topRight.X : right, _bounds.Y);
        var topRightRight = new PaintPoint(right, _topRight.Active ? _topRight.Y : _bounds.Y);
        var bottomRightRight = new PaintPoint(right, _bottomRight.Active ? _bottomRight.Y : bottom);
        var bottomRightBottom = new PaintPoint(_bottomRight.Active ? _bottomRight.X : right, bottom);
        var bottomLeftBottom = new PaintPoint(_bottomLeft.Active ? _bottomLeft.X : _bounds.X, bottom);
        var bottomLeftLeft = new PaintPoint(_bounds.X, _bottomLeft.Active ? _bottomLeft.Y : bottom);
        var topLeftLeft = new PaintPoint(_bounds.X, _topLeft.Active ? _topLeft.Y : _bounds.Y);
        if (quad.Contains(topLeftTop) || quad.Contains(topRightTop) || quad.Contains(topRightRight)
            || quad.Contains(bottomRightRight) || quad.Contains(bottomRightBottom) || quad.Contains(bottomLeftBottom)
            || quad.Contains(bottomLeftLeft) || quad.Contains(topLeftLeft)) return true;
        if (quad.IntersectsSegment(topLeftTop, topRightTop) || quad.IntersectsSegment(topRightRight, bottomRightRight)
            || quad.IntersectsSegment(bottomRightBottom, bottomLeftBottom) || quad.IntersectsSegment(bottomLeftLeft, topLeftLeft)) return true;
        return EllipseIntersects(quad, _topLeft, false, false) || EllipseIntersects(quad, _topRight, true, false)
            || EllipseIntersects(quad, _bottomRight, true, true) || EllipseIntersects(quad, _bottomLeft, false, true);
    }
    private bool ContainsShape(PaintPoint point)
    {
        if (!_bounds.Contains(point.X, point.Y)) return false;
        if (_topLeft.Active && point.X < _topLeft.X && point.Y < _topLeft.Y)
            return _topLeft.Intersects(point.X - _topLeft.X, point.Y - _topLeft.Y);
        if (_topRight.Active && point.X > _topRight.X && point.Y < _topRight.Y)
            return _topRight.Intersects(point.X - _topRight.X, point.Y - _topRight.Y);
        if (_bottomRight.Active && point.X > _bottomRight.X && point.Y > _bottomRight.Y)
            return _bottomRight.Intersects(point.X - _bottomRight.X, point.Y - _bottomRight.Y);
        if (_bottomLeft.Active && point.X < _bottomLeft.X && point.Y > _bottomLeft.Y)
            return _bottomLeft.Intersects(point.X - _bottomLeft.X, point.Y - _bottomLeft.Y);
        return true;
    }
    private static bool EllipseIntersects(in HitQueryQuad quad, Corner corner, bool right, bool bottom)
    {
        if (!corner.Active) return false;
        for (int i = 0; i < 4; i++)
        {
            var start = quad[i]; var end = quad[(i + 1) & 3];
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double x = start.X - corner.X, y = start.Y - corner.Y;
            double a = dx * dx * corner.InverseX2 + dy * dy * corner.InverseY2;
            double b = 2 * (x * dx * corner.InverseX2 + y * dy * corner.InverseY2);
            double c = x * x * corner.InverseX2 + y * y * corner.InverseY2 - 1;
            double discriminant = b * b - 4 * a * c;
            if (a == 0 || discriminant < 0) continue;
            double root = Math.Sqrt(discriminant);
            if (OnArc((-b - root) / (2 * a), start, dx, dy, corner, right, bottom)
                || OnArc((-b + root) / (2 * a), start, dx, dy, corner, right, bottom)) return true;
        }
        return false;
    }
    private static bool OnArc(double t, PaintPoint start, double dx, double dy, Corner corner, bool right, bool bottom)
    {
        if (t < 0 || t > 1 || !double.IsFinite(t)) return false;
        double x = start.X + t * dx, y = start.Y + t * dy;
        return (right ? x >= corner.X : x <= corner.X) && (bottom ? y >= corner.Y : y <= corner.Y);
    }
}
