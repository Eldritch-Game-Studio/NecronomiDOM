using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>New clip geometry on the existing isolated, tight device-ink, retained composite backend.</summary>
public partial class EllipticalClipGroup : PolygonComposite
{
    private static readonly StringName ClipRectParameter = new("clip_rect");
    private static readonly StringName TopRadiiParameter = new("top_radii");
    private static readonly StringName BottomRadiiParameter = new("bottom_radii");
    private static readonly StringName EmptyParameter = new("empty_clip");
    private EllipticalClipGeometry _geometry;
    private bool _initialized;
    private Rect _rasterBounds;
    private Vector2? _rasterOrigin;
    public EllipticalClipGeometry Geometry => _geometry;
    protected override Rect2? ClipBounds => Bounds(_rasterBounds);
    protected override bool EmptyClip => _geometry.IsEmpty;

    internal EllipticalClipGroup(BeginEllipticalClipRun run)
    {
        CompositeMaterial.Shader = WebPaintShaders.EllipticalClip;
        Update(run);
    }
    internal void Update(BeginEllipticalClipRun run)
    {
        if (_initialized && _geometry == run.Geometry) return;
        var bounds = run.Bounds; var radii = run.Radii;
        // Blink pixel-snaps the paint rectangle in CSS space before its affine transform.
        // Keep the DOM/hit geometry subpixel; rounded clips retain their existing contour.
        if (radii == default) bounds = ImageLayout.RasterBounds(bounds);
        _rasterBounds = bounds;
        CompositeMaterial.SetShaderParameter(ClipRectParameter,new Vector4(Float(bounds.X),Float(bounds.Y),Float(bounds.Width),Float(bounds.Height)));
        CompositeMaterial.SetShaderParameter(TopRadiiParameter,new Vector4(Float(radii.TopLeft.X),Float(radii.TopLeft.Y),Float(radii.TopRight.X),Float(radii.TopRight.Y)));
        CompositeMaterial.SetShaderParameter(BottomRadiiParameter,new Vector4(Float(radii.BottomRight.X),Float(radii.BottomRight.Y),Float(radii.BottomLeft.X),Float(radii.BottomLeft.Y)));
        CompositeMaterial.SetShaderParameter(EmptyParameter,run.Geometry.IsEmpty);
        _geometry = run.Geometry; _initialized = true;
    }
    protected override void ConfigureDeviceCoverage()
    {
        // Isolated targets translate device coordinates by whole pixels. Restore the
        // root raster origin so nesting does not restart Chromium's coverage tile grid.
        Vector2 origin = Vector2.Zero;
        Viewport viewport = GetViewport();
        while (viewport is SubViewport && viewport.GetParent() is PolygonComposite parent)
        {
            origin += parent.DevicePaintBounds.Position;
            viewport = parent.GetViewport();
        }
        if (_rasterOrigin == origin) return;
        CompositeMaterial.SetShaderParameter("rect_raster_origin", origin);
        _rasterOrigin = origin;
    }
    private static float Float(double value) => PolygonPaint.FiniteFloat(value);
    private static Rect2 Bounds(Rect bounds) => new(Float(bounds.X),Float(bounds.Y),Float(bounds.Width),Float(bounds.Height));
}

/// <summary>
/// One new-type store per painter. Detach before ordinary child cleanup; end rebuild only trims.
/// The sole ordered dispatcher measures ALL composite kinds then fits ALL in display preorder,
/// never by store order, because nested viewport canvas transforms are established outer-first.
/// </summary>
public sealed class EllipticalClipStore : IDisposable
{
    private readonly List<EllipticalClipGroup> _groups = new();
    private int _cursor;
    public void DetachBeforeClear()
    {
        for (int i = _groups.Count - 1; i >= 0; i--) _groups[i].GetParent()?.RemoveChild(_groups[i]);
    }
    public void BeginRebuild() => _cursor = 0;
    public EllipticalClipGroup CreateContext(BeginEllipticalClipRun run)
    {
        EllipticalClipGroup group;
        if (_cursor < _groups.Count) { group = _groups[_cursor]; group.Update(run); }
        else { group = new EllipticalClipGroup(run); _groups.Add(group); }
        _cursor++; group.BeginRebuild(); return group;
    }
    public void EndRebuild()
    {
        for (int i = _groups.Count - 1; i >= _cursor; i--) _groups[i].Free();
        if (_cursor < _groups.Count) _groups.RemoveRange(_cursor,_groups.Count-_cursor);
    }
    public void Dispose()
    {
        DetachBeforeClear();
        for (int i = _groups.Count - 1; i >= 0; i--) _groups[i].Free();
        _groups.Clear(); _cursor = 0;
    }
}
