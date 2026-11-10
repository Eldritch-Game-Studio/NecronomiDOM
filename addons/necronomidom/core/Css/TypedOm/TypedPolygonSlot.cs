using System.Text;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.TypedOm;

/// <summary>Declaration-owned independent coordinates. Used paint geometry is always a separate immutable snapshot.</summary>
internal sealed class TypedPolygonSlot : CssValue
{
    private readonly CSSPolygonPoint[] _points;
    private readonly CSSPolygonValue _current;
    internal TypedPolygonSlot(CSSPolygonValue value)
    {
        _points = new CSSPolygonPoint[value.Points.Count];
        Update(value);
        _current = new CSSPolygonValue(_points);
    }
    internal CSSPolygonValue Current => _current;
    internal long Revision { get; private set; }
    internal bool SameShape(CSSPolygonValue value) => _points.Length == value.Points.Count;
    internal void Update(CSSPolygonValue value)
    {
        for (int i = 0; i < _points.Length; i++) _points[i] = value.Points[i];
        Revision++;
    }
    internal CSSPolygonValue Snapshot() => new((CSSPolygonPoint[])_points.Clone());
    public override void AppendTo(StringBuilder text) => _current.AppendTo(text);
}
