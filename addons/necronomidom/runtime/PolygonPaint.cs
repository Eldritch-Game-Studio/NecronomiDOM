using System;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Indexed triangle fans, with a retained vertex buffer updated only when DOM geometry changes.</summary>
public partial class PolygonPaint : Node2D, INativePaintBounds
{
    private ArrayMesh? _mesh;
    private ShaderMaterial? _fill;
    private byte[] _vertexBytes = Array.Empty<byte>();
    private int _vertexCount;
    private ConvexPolygon? _geometry;
    private Rect _bounds;
    private Rgba _color;
    private bool _empty;
    private Rect2 _paintBounds;

    internal PolygonPaint(PolygonRun run) => Update(run);
    public int SurfaceBuildCount { get; private set; }
    public int VertexUploadCount { get; private set; }
    public Rect2 PaintBounds => _empty ? default : _paintBounds;

    internal void Update(PolygonRun run)
    {
        if (_geometry == null || _color != run.Color)
        {
            SelfModulate = new Color(run.Color.R, run.Color.G, run.Color.B, run.Color.A);
            _color = run.Color;
        }
        bool empty = run.Geometry.IsEmpty || run.Bounds.Width == 0 || run.Bounds.Height == 0;
        if (_geometry == null || _empty != empty) { Visible = !empty; _empty = empty; }
        if (empty) { _geometry = run.Geometry; _bounds = run.Bounds; return; }
        if (_mesh != null && _bounds == run.Bounds && SameGeometry(_geometry, run.Geometry)) return;
        bool newMaterial = _fill == null;
        if (newMaterial)
        {
            _fill = new ShaderMaterial { Shader = WebPaintShaders.PolygonFill };
            Material = _fill;
        }
        if (newMaterial || _bounds != run.Bounds)
            _fill!.SetShaderParameter("reference_bounds", new Vector4(FiniteFloat(run.Bounds.X), FiniteFloat(run.Bounds.Y), FiniteFloat(run.Bounds.Width), FiniteFloat(run.Bounds.Height)));
        int count = run.Geometry.Count;
        bool newSurface = _mesh == null || count != _vertexCount;
        Vector3[]? vertices = newSurface ? new Vector3[count] : null;
        if (newSurface) _vertexBytes = new byte[count * 12];
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            PaintPoint p = run.Geometry[i];
            float x = FiniteFloat(run.Bounds.X + p.X), y = FiniteFloat(run.Bounds.Y + p.Y);
            BitConverter.TryWriteBytes(_vertexBytes.AsSpan(i * 12, 4), x);
            BitConverter.TryWriteBytes(_vertexBytes.AsSpan(i * 12 + 4, 4), y);
            // Z stays zero in the retained native 3-float vertex stride.
            if (vertices != null) vertices[i] = new Vector3(x, y, 0);
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        if (newSurface)
        {
            var indices = new int[(count - 2) * 3];
            for (int i = 0; i < count - 2; i++)
            { indices[i * 3] = 0; indices[i * 3 + 1] = i + 1; indices[i * 3 + 2] = i + 2; }
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices!;
            arrays[(int)Mesh.ArrayType.Index] = indices;
            _mesh ??= new ArrayMesh(); _mesh.ClearSurfaces();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
            SurfaceBuildCount++;
            _vertexCount = count; QueueRedraw();
        }
        else _mesh!.SurfaceUpdateVertexRegion(0, 0, _vertexBytes);
        // Fit/composite only the background's actual border-box intersection, not out-of-box CSS clip vertices.
        minX = Math.Clamp(minX, FiniteFloat(run.Bounds.X), FiniteFloat(run.Bounds.X + run.Bounds.Width));
        minY = Math.Clamp(minY, FiniteFloat(run.Bounds.Y), FiniteFloat(run.Bounds.Y + run.Bounds.Height));
        maxX = Math.Clamp(maxX, minX, FiniteFloat(run.Bounds.X + run.Bounds.Width));
        maxY = Math.Clamp(maxY, minY, FiniteFloat(run.Bounds.Y + run.Bounds.Height));
        _paintBounds = new Rect2(minX, minY, maxX - minX, maxY - minY);
        _mesh!.CustomAabb = new Aabb(new Vector3(minX, minY, 0), new Vector3(maxX - minX, maxY - minY, 0));
        VertexUploadCount++;
        RenderingServer.CanvasItemSetCustomRect(GetCanvasItem(), true, new Rect2(minX, minY, maxX - minX, maxY - minY));
        _geometry = run.Geometry; _bounds = run.Bounds;
    }

    public override void _Draw() { if (_mesh != null) DrawMesh(_mesh, null); }
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Material = null; _fill?.Dispose(); _mesh?.Dispose(); _fill = null; _mesh = null;
    }
    internal static bool SameGeometry(ConvexPolygon? previous, ConvexPolygon current)
    {
        if (ReferenceEquals(previous, current)) return true;
        if (previous == null || previous.Count != current.Count) return false;
        for (int i = 0; i < current.Count; i++) if (previous[i] != current[i]) return false;
        return true;
    }
    internal static float FiniteFloat(double value)
    {
        float result = (float)value;
        if (!float.IsFinite(result)) throw new NotSupportedException("Paint geometry exceeds the native finite coordinate range.");
        return result;
    }
}

/// <summary>Convex coverage applied to an isolated subtree, never a shared screen/backbuffer sampler.</summary>
public partial class PolygonClipGroup : PolygonComposite
{
    private Image? _image;
    private ImageTexture? _edges;
    private ConvexPolygon? _geometry;
    private Rect _bounds;
    private Rect2 _clipBounds;
    protected override Rect2? ClipBounds => _clipBounds;
    protected override bool EmptyClip => _geometry?.IsEmpty ?? true;
    internal PolygonClipGroup(BeginPolygonClipRun run) => Update(run);
    internal void Update(BeginPolygonClipRun run)
    {
        if (_bounds == run.Bounds && PolygonPaint.SameGeometry(_geometry, run.Geometry)) return;
        ConvexPolygon polygon = run.Geometry;
        CompositeMaterial.SetShaderParameter("empty_polygon", polygon.IsEmpty);
        if (polygon.IsEmpty)
        {
            _geometry = polygon; _bounds = run.Bounds; _clipBounds = default; return;
        }
        bool resize = _image == null || _image.GetWidth() != polygon.Count;
        if (resize) { _image?.Dispose(); _image = Image.CreateEmpty(polygon.Count, 1, false, Image.Format.Rgbaf); }
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            PaintPoint a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            double x = run.Bounds.X + a.X, y = run.Bounds.Y + a.Y;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double nx = length == 0 ? 0 : -dy * polygon.Winding / length;
            double ny = length == 0 ? 0 : dx * polygon.Winding / length;
            double offset = length == 0 ? 1 : -nx * x - ny * y;
            _image!.SetPixel(i, 0, new Color(PolygonPaint.FiniteFloat(nx), PolygonPaint.FiniteFloat(ny), PolygonPaint.FiniteFloat(offset), 0));
        }
        _clipBounds = new Rect2(PolygonPaint.FiniteFloat(minX), PolygonPaint.FiniteFloat(minY),
            PolygonPaint.FiniteFloat(maxX - minX), PolygonPaint.FiniteFloat(maxY - minY));
        if (resize)
        {
            _edges?.Dispose(); _edges = ImageTexture.CreateFromImage(_image!);
            CompositeMaterial.SetShaderParameter("edges", _edges);
            CompositeMaterial.SetShaderParameter("edge_count", polygon.Count);
        }
        else _edges!.Update(_image!);
        _geometry = polygon; _bounds = run.Bounds;
    }
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) { _edges?.Dispose(); _image?.Dispose(); _edges = null; _image = null; }
        base._Notification(what);
    }
}

public partial class RoundedRectClipGroup : PolygonComposite
{
    private Rect _bounds;
    private double _radius = -1;
    protected override Rect2? ClipBounds => new Rect2(PolygonPaint.FiniteFloat(_bounds.X), PolygonPaint.FiniteFloat(_bounds.Y),
        PolygonPaint.FiniteFloat(_bounds.Width), PolygonPaint.FiniteFloat(_bounds.Height));
    protected override bool EmptyClip => _bounds.Width == 0 || _bounds.Height == 0;
    internal RoundedRectClipGroup(Rect bounds, double radius)
    {
        CompositeMaterial.SetShaderParameter("rounded_rect", true); Update(bounds, radius);
    }
    internal void Update(Rect bounds, double radius)
    {
        if (_bounds == bounds && _radius == radius) return;
        CompositeMaterial.SetShaderParameter("clip_rect", new Vector4(PolygonPaint.FiniteFloat(bounds.X), PolygonPaint.FiniteFloat(bounds.Y),
            PolygonPaint.FiniteFloat(bounds.Width), PolygonPaint.FiniteFloat(bounds.Height)));
        CompositeMaterial.SetShaderParameter("clip_radius", PolygonPaint.FiniteFloat(radius));
        CompositeMaterial.SetShaderParameter("empty_rect", bounds.Width == 0 || bounds.Height == 0);
        _bounds = bounds; _radius = radius;
    }
}

public partial class PolygonOpacityGroup : PolygonComposite
{
    private float _opacity = -1;
    internal PolygonOpacityGroup(float opacity) => Update(opacity);
    internal void Update(float opacity)
    {
        if (_opacity == opacity) return;
        SelfModulate = new Color(1, 1, 1, opacity); _opacity = opacity;
        InvalidateAncestorTargets();
    }
}
