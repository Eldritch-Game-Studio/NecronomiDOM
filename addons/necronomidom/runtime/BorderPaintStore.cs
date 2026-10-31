#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Retained border materials and transform contexts. Rebuild updates data; frames allocate nothing.</summary>
public sealed class BorderPaintStore : IDisposable
{
    private readonly List<BorderItem> _borders = new();
    private readonly List<Node2D> _contexts = new();
    private readonly StringName _boxSize = new("box_size"), _widths = new("widths"),
        _radiusX = new("radius_x"), _radiusY = new("radius_y"), _background = new("background_color"),
        _top = new("top_color"), _right = new("right_color"), _bottom = new("bottom_color"), _left = new("left_color");
    private int _borderCursor, _contextCursor;
    private bool _disposed;
    private sealed record BorderItem(MeshInstance2D Node, QuadMesh Mesh, ShaderMaterial Material);
    public void DetachBeforeClear()
    {
        foreach (var item in _borders) Detach(item.Node);
        foreach (var item in _contexts) Detach(item);
    }
    private static void Detach(Node node)
    { if (node.GetParent() is { } parent) parent.RemoveChild(node); }
    public void BeginRebuild()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _borderCursor = _contextCursor = 0;
    }
    public CanvasItem Create(BorderBoxRun run)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Validate(run.Borders.Top); Validate(run.Borders.Right); Validate(run.Borders.Bottom); Validate(run.Borders.Left);
        BorderItem item;
        if (_borderCursor == _borders.Count)
        {
            var mesh = new QuadMesh();
            var material = new ShaderMaterial { Shader = WebPaintShaders.AsymmetricBorder };
            item = new(new MeshInstance2D { Mesh = mesh, Material = material }, mesh, material);
            _borders.Add(item);
        }
        else item = _borders[_borderCursor];
        _borderCursor++;
        var bounds = run.Bounds;
        item.Node.Position = new((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        item.Node.Visible = bounds.Width > 0 && bounds.Height > 0;
        item.Mesh.Size = new((float)Math.Max(0, bounds.Width), (float)Math.Max(0, bounds.Height));
        var material2 = item.Material;
        material2.SetShaderParameter(_boxSize, new Vector2((float)bounds.Width, (float)bounds.Height));
        var b = run.Borders;
        material2.SetShaderParameter(_widths, new Vector4((float)b.Top.Width, (float)b.Right.Width, (float)b.Bottom.Width, (float)b.Left.Width));
        var r = run.Radii.Normalize(bounds.Width, bounds.Height);
        material2.SetShaderParameter(_radiusX, new Vector4((float)r.TopLeft.X, (float)r.TopRight.X, (float)r.BottomRight.X, (float)r.BottomLeft.X));
        material2.SetShaderParameter(_radiusY, new Vector4((float)r.TopLeft.Y, (float)r.TopRight.Y, (float)r.BottomRight.Y, (float)r.BottomLeft.Y));
        material2.SetShaderParameter(_background, Color(run.Background));
        material2.SetShaderParameter(_top, Color(b.Top.Color));
        material2.SetShaderParameter(_right, Color(b.Right.Color));
        material2.SetShaderParameter(_bottom, Color(b.Bottom.Color));
        material2.SetShaderParameter(_left, Color(b.Left.Color));
        return item.Node;
    }
    public CanvasItem CreateContext(BeginTransformRun run)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Node2D node;
        if (_contextCursor == _contexts.Count) { node = new Node2D(); _contexts.Add(node); }
        else node = _contexts[_contextCursor];
        _contextCursor++;
        node.Transform = ToNative(run.Transform);
        return node;
    }
    /// <summary>No DPR conversion: native canvas inherits document CSS-pixel scaling from its host.</summary>
    public static Transform2D ToNative(Affine2D matrix) => new(new Vector2((float)matrix.A, (float)matrix.B),
        new Vector2((float)matrix.C, (float)matrix.D), new Vector2((float)matrix.E, (float)matrix.F));
    public void EndRebuild()
    {
        // Unused retained items stay detached for reuse; attached contexts contain this rebuild's actual descendants.
        for (int i = _borderCursor; i < _borders.Count; i++) Detach(_borders[i].Node);
        for (int i = _contextCursor; i < _contexts.Count; i++) Detach(_contexts[i]);
    }
    private static Godot.Color Color(Rgba rgba) => new(rgba.R, rgba.G, rgba.B, rgba.A);
    private static void Validate(BorderSide side)
    {
        if (side.Style is not ("solid" or "none" or "hidden") || side.Width < 0 || !double.IsFinite(side.Width))
            throw new NotSupportedException("Native border style/width unavailable: " + side.Style);
        if (side.Style is "none" or "hidden" && side.Width != 0)
            throw new ArgumentException("None/hidden borders must have zero used width.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        DetachBeforeClear();
        foreach (var item in _borders) { item.Node.Free(); item.Material.Dispose(); item.Mesh.Dispose(); }
        foreach (var item in _contexts) item.Free();
        _boxSize.Dispose(); _widths.Dispose(); _radiusX.Dispose(); _radiusY.Dispose(); _background.Dispose();
        _top.Dispose(); _right.Dispose(); _bottom.Dispose(); _left.Dispose();
        _borders.Clear(); _contexts.Clear(); _disposed = true;
    }
}
