#nullable enable
using System;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class PolygonPaint
{
    private readonly bool _singleFill;
    private int _singleFillEdgeCount = -1;
    public bool IsSingleFill => _singleFill;

    private void WriteSingleFillColor()
    {
        if (_fill == null) return;
        // Match the existing polygon fill's raw self modulation, not the explicit
        // sRGB conversion used by other DocumentPaint primitives.
        _fill.SetShaderParameter("fill_color", new Color(_color.R, _color.G, _color.B, _color.A));
    }

    private void UpdateSingleFillEdges(PolygonRun run)
    {
        if (!_singleFill) return;
        Projection edges = default;
        for (int i = 0; i < run.Geometry.Count; i++)
        {
            PaintPoint a = run.Geometry[i], b = run.Geometry[(i + 1) % run.Geometry.Count];
            double x = run.Bounds.X + a.X, y = run.Bounds.Y + a.Y;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double nx = length == 0 ? 0 : -dy * run.Geometry.Winding / length;
            double ny = length == 0 ? 0 : dx * run.Geometry.Winding / length;
            double offset = length == 0 ? 1 : -nx * x - ny * y;
            edges[i] = new Vector4(FiniteFloat(nx), FiniteFloat(ny), FiniteFloat(offset), 0);
        }
        _fill!.SetShaderParameter("polygon_edges", edges);
        if (_singleFillEdgeCount != run.Geometry.Count)
        {
            _singleFillEdgeCount = run.Geometry.Count;
            _fill.SetShaderParameter("edge_count", _singleFillEdgeCount);
        }
    }
}
