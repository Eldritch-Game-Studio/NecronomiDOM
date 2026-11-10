using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm;

/// <summary>A managed polygon vertex with independent pixel or percentage coordinates.</summary>
public readonly record struct CSSPolygonPoint(CSSUnitValue X, CSSUnitValue Y);

/// <summary>Managed Typed OM extension for a nonzero polygon; retains reusable caller storage until assignment.</summary>
public readonly struct CSSPolygonValue
{
    private readonly IReadOnlyList<CSSPolygonPoint>? _points;
    public CSSPolygonValue(IReadOnlyList<CSSPolygonPoint> points)
    {
        _points = points ?? throw new ArgumentNullException(nameof(points));
        Validate();
    }
    public IReadOnlyList<CSSPolygonPoint> Points => _points ?? Array.Empty<CSSPolygonPoint>();

    internal void Validate()
    {
        if (Points.Count < 3) throw new DomTypeError("A polygon requires at least three vertices.");
        for (int i = 0; i < Points.Count; i++)
        {
            var point = Points[i];
            if (!Coordinate(point.X) || !Coordinate(point.Y))
                throw new DomTypeError("Polygon coordinates must be finite pixels or percentages.");
        }
    }
    private static bool Coordinate(CSSUnitValue value) =>
        double.IsFinite(value.Value) && value.Unit is "px" or "percent";

    public override string ToString() { var text = new StringBuilder(); AppendTo(text); return text.ToString(); }
    internal void AppendTo(StringBuilder text)
    {
        text.Append("polygon(");
        for (int i = 0; i < Points.Count; i++)
        {
            if (i > 0) text.Append(", ");
            text.Append(Points[i].X.ToString()).Append(' ').Append(Points[i].Y.ToString());
        }
        text.Append(')');
    }
}
