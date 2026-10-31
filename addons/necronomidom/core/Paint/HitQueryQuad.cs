using System;

namespace NecronomiDom.Paint;

/// <summary>The inverse image of the browser's one-CSS-pixel viewport query, not a new local axis-aligned pixel.
/// Four value-type vertices and separating-axis tests allocate nothing during pointer sampling.</summary>
internal readonly struct HitQueryQuad
{
    internal readonly PaintPoint P0, P1, P2, P3;
    internal HitQueryQuad(PaintPoint origin, PaintPoint horizontal, PaintPoint vertical)
    {
        P0 = origin; P1 = new(origin.X + horizontal.X, origin.Y + horizontal.Y);
        P2 = new(P1.X + vertical.X, P1.Y + vertical.Y); P3 = new(origin.X + vertical.X, origin.Y + vertical.Y);
    }
    internal PaintPoint this[int index] => index switch { 0 => P0, 1 => P1, 2 => P2, _ => P3 };
    internal bool Contains(PaintPoint point)
    {
        double direction = Cross(P0, P1, P2);
        for (int i = 0; i < 4; i++)
            if (Cross(this[i], this[(i + 1) & 3], point) * direction < 0) return false;
        return direction != 0;
    }
    internal bool Intersects(Rect bounds, bool clipsX = true, bool clipsY = true)
    {
        if (bounds.Width > 0 && bounds.Height > 0 && P0.X > bounds.X && P0.X < bounds.X + bounds.Width
            && P0.Y > bounds.Y && P0.Y < bounds.Y + bounds.Height) return true;
        if (clipsX && (bounds.Width <= 0 || Max(P0.X, P1.X, P2.X, P3.X) <= bounds.X
            || Min(P0.X, P1.X, P2.X, P3.X) >= bounds.X + bounds.Width)) return false;
        if (clipsY && (bounds.Height <= 0 || Max(P0.Y, P1.Y, P2.Y, P3.Y) <= bounds.Y
            || Min(P0.Y, P1.Y, P2.Y, P3.Y) >= bounds.Y + bounds.Height)) return false;
        if (!clipsX || !clipsY) return true;
        // X/Y plus the two distinct quad edge normals form the complete rectangle/quad SAT.
        return !Separated(bounds, P1.X - P0.X, P1.Y - P0.Y)
            && !Separated(bounds, P3.X - P0.X, P3.Y - P0.Y);
    }
    private bool Separated(Rect bounds, double edgeX, double edgeY)
    {
        double x = -edgeY, y = edgeX;
        double q0 = Dot(P0, x, y), q1 = Dot(P1, x, y), q2 = Dot(P2, x, y), q3 = Dot(P3, x, y);
        double a = bounds.X * x + bounds.Y * y;
        double b = a + bounds.Width * x, c = a + bounds.Height * y, d = b + bounds.Height * y;
        return Max(q0, q1, q2, q3) <= Min(a, b, c, d) || Min(q0, q1, q2, q3) >= Max(a, b, c, d);
    }
    internal bool Intersects(ConvexPolygon polygon, Rect bounds)
    {
        if (polygon.IsEmpty) return false;
        for (int i = 0; i < 4; i++)
            if (polygon.Contains(this[i].X - bounds.X, this[i].Y - bounds.Y)) return true;
        for (int i = 0; i < polygon.Count; i++)
        {
            var vertex = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            var a = new PaintPoint(bounds.X + vertex.X, bounds.Y + vertex.Y);
            var b = new PaintPoint(bounds.X + next.X, bounds.Y + next.Y);
            if (Contains(a) || IntersectsSegment(a, b)) return true;
        }
        return false;
    }
    internal bool IntersectsSegment(PaintPoint a, PaintPoint b)
    {
        for (int i = 0; i < 4; i++)
        {
            var c = this[i]; var d = this[(i + 1) & 3];
            double ca = Cross(c, d, a), cb = Cross(c, d, b), ac = Cross(a, b, c), ad = Cross(a, b, d);
            if (ca * cb <= 0 && ac * ad <= 0
                && Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X))
                && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) return true;
        }
        return false;
    }
    private static double Dot(PaintPoint point, double x, double y) => point.X * x + point.Y * y;
    private static double Cross(PaintPoint a, PaintPoint b, PaintPoint c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static double Min(double a, double b, double c, double d) => Math.Min(Math.Min(a, b), Math.Min(c, d));
    private static double Max(double a, double b, double c, double d) => Math.Max(Math.Max(a, b), Math.Max(c, d));
}
