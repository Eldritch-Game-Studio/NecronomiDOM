#nullable enable
using System;
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>Actual native ink geometry in the item's local coordinate space, before device-pixel fringe.</summary>
public interface INativePaintBounds
{
    Rect2 PaintBounds { get; }
}

/// <summary>
/// A retained, transparent 2D isolation target. The proxy paints its child texture exactly once;
/// content lives in its own World2D, not the parent's shared CanvasGroup backbuffer.
/// DOM/input ownership remains with DocumentHost, outside these render-only viewports.
/// </summary>
[GodotClass]
public partial class PolygonComposite : Node2D
{
    private readonly World2D _world = new();
    private readonly SubViewport _target;
    // This render-only World2D is a separate interpolation scope; UI poses advance per process frame.
    private readonly Node2D _content = new() { PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
    private readonly ViewportTexture _texture;
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _uv = new Vector2[4];
    private readonly Color[] _colors = [new Color(1, 1, 1, 1)];
    private Rect2 _logicalBounds;
    private bool _measured, _hasPaint;
    private Vector2I _capacity = new(2, 2);
    private Rect2 _deviceBounds;
    private Transform2D _deviceTransform;
    protected ShaderMaterial CompositeMaterial { get; }
    protected bool LinearTarget { get; init; }
    protected virtual Rect2? ClipBounds => null;
    protected virtual bool EmptyClip => false;
    protected virtual void ConfigureDeviceCoverage() { }
    public CanvasItem Content => _content;
    public Rect2 DevicePaintBounds => _deviceBounds;
    public Vector2I TargetCapacity => _capacity;
    public int TargetResizeCount { get; private set; }
    public int TargetUpdateCount { get; private set; }

    protected PolygonComposite()
    {
        _target = new SubViewport
        {
            Size = _capacity, TransparentBg = true, Disable3D = true,
            GuiDisableInput = true, World2D = _world,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            HandleInputLocally = false
        };
        AddChild(_target); _target.AddChild(_content);
        _texture = _target.GetTexture();
        CompositeMaterial = new ShaderMaterial { Shader = WebPaintShaders.PolygonClip };
        Material = CompositeMaterial;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    internal void BeginRebuild() => _measured = false;
    internal void MeasurePaint()
    {
        if (_measured) return;
        _measured = true; _hasPaint = false;
        if (!EmptyClip) Collect(_content, Transform2D.Identity, ref _logicalBounds, ref _hasPaint);
        if (_hasPaint && ClipBounds is Rect2 clip)
        {
            _logicalBounds = _logicalBounds.Intersection(clip);
            _hasPaint = _logicalBounds.Size.X > 0 && _logicalBounds.Size.Y > 0;
        }
    }

    private static void Collect(Node parent, Transform2D transform, ref Rect2 bounds, ref bool hasPaint)
    {
        for (int childIndex = 0; childIndex < parent.GetChildCount(); childIndex++)
        {
            Node child = parent.GetChild(childIndex);
            if (child is not CanvasItem item) throw new NotSupportedException("Unknown native paint child: " + child.GetType().FullName);
            if (!item.Visible && item is not PolygonComposite) continue;
            if (item is not Node2D node) throw new NotSupportedException("Native paint geometry requires a typed Node2D: " + item.GetType().FullName);
            Transform2D local = transform * node.Transform;
            Rect2 rect;
            switch (item)
            {
                case PolygonComposite group:
                    group.MeasurePaint();
                    if (!group._hasPaint) continue;
                    rect = group._logicalBounds;
                    break;
                case INativePaintBounds ink: rect = ink.PaintBounds; break;
                case MultiMeshInstance2D multi when multi.Multimesh != null:
                    Aabb box = multi.Multimesh.CustomAabb;
                    rect = new Rect2(box.Position.X, box.Position.Y, box.Size.X, box.Size.Y);
                    break;
                case MeshInstance2D mesh when mesh.Mesh != null:
                    Aabb geometry = mesh.Mesh.GetAabb();
                    rect = new Rect2(geometry.Position.X, geometry.Position.Y, geometry.Size.X, geometry.Size.Y);
                    break;
                case Node2D container when container.GetType() == typeof(Node2D):
                    // A transform-only carrier has no ink of its own, including an empty subtree.
                    Collect(container, local, ref bounds, ref hasPaint); continue;
                default:
                    if (item.GetChildCount() == 0) throw new NotSupportedException("Missing actual native paint bounds: " + item.GetType().FullName);
                    Collect(item, local, ref bounds, ref hasPaint); continue;
            }
            if (rect.Size.X <= 0 || rect.Size.Y <= 0) continue;
            Rect2 transformed = local * rect;
            bounds = hasPaint ? bounds.Merge(transformed) : transformed;
            hasPaint = true;
        }
    }

    /// <summary>Called in outer-to-inner paint order after all actual child bounds are measured.</summary>
    internal void FitTarget()
    {
        MeasurePaint();
        Visible = _hasPaint;
        if (!_hasPaint)
        {
            _deviceBounds = default;
            _target.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            return;
        }
        // Includes actual viewport/window/stretch/global-canvas and ancestor affine
        // transforms exactly once; never include the desktop screen position.
        Transform2D device = GetViewportTransform() * GetGlobalTransform();
        if (!float.IsFinite(device.Determinant()) || device.Determinant() == 0)
            throw new NotSupportedException("Isolation context requires a finite invertible device transform.");
        ConfigureDeviceCoverage();
        _target.UseHdr2D = LinearTarget || GetViewport().UseHdr2D;
        // The parent render target is an actual device clipping boundary. Do not
        // allocate offscreen textures for overflow pixels it cannot display.
        Vector2 physicalSize = GetViewport().GetTexture().GetSize();
        Rect2 painted = (device * _logicalBounds).Intersection(new Rect2(Vector2.Zero, physicalSize));
        if (painted.Size.X <= 0 || painted.Size.Y <= 0)
        {
            Visible = false; _deviceBounds = default;
            _target.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; return;
        }
        // One physical pixel covers raster/coverage fringe, independent of CSS scale or DPR.
        Vector2 origin = new(MathF.Floor(painted.Position.X) - 1, MathF.Floor(painted.Position.Y) - 1);
        Vector2 end = new(MathF.Ceiling(painted.End.X) + 1, MathF.Ceiling(painted.End.Y) + 1);
        var required = new Vector2I(checked((int)(end.X - origin.X)), checked((int)(end.Y - origin.Y)));
        if (required.X <= 0 || required.Y <= 0) throw new InvalidOperationException("Non-empty isolation context has no device paint extent.");
        if (required.X > _capacity.X || required.Y > _capacity.Y)
        {
            // Retain a small capacity quantum across subpixel vertex changes; never reallocate on shrink.
            _capacity = new Vector2I(Math.Max(_capacity.X, checked((required.X + 15) / 16 * 16)), Math.Max(_capacity.Y, checked((required.Y + 15) / 16 * 16)));
            _target.Size = _capacity; TargetResizeCount++;
        }
        Rect2 next = new(origin, end - origin);
        bool changed = _deviceBounds != next || _deviceTransform != device;
        _deviceBounds = next; _deviceTransform = device;
        _target.CanvasTransform = new Transform2D(0, -origin) * device;
        Transform2D inverse = device.AffineInverse();
        _quad[0] = inverse * origin;
        _quad[1] = inverse * new Vector2(end.X, origin.Y);
        _quad[2] = inverse * end;
        _quad[3] = inverse * new Vector2(origin.X, end.Y);
        Vector2 extent = next.Size / new Vector2(_capacity.X, _capacity.Y);
        _uv[0] = Vector2.Zero; _uv[1] = new Vector2(extent.X, 0); _uv[2] = extent; _uv[3] = new Vector2(0, extent.Y);
        RenderingServer.CanvasItemSetCustomRect(GetCanvasItem(), true, inverse * next);
        // SubViewport render dependencies are child texture -> parent texture -> window.
        // Request only dirty rebuild frames, never a continuously redrawn isolation target.
        _target.RenderTargetUpdateMode = SubViewport.UpdateMode.Once; TargetUpdateCount++;
        if (changed || TargetUpdateCount == 1) QueueRedraw();
    }
    /// <summary>Changing this proxy's modulation leaves its own content intact,
    /// but its ancestors must recapture the changed draw into their textures.</summary>
    protected void InvalidateAncestorTargets()
    {
        if (!IsInsideTree()) return;
        Viewport parent = GetViewport();
        while (parent is SubViewport && parent.GetParent() is PolygonComposite owner)
        {
            if (owner._hasPaint)
            {
                owner._target.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                owner.TargetUpdateCount++;
            }
            parent = owner.GetViewport();
        }
    }


    public override void _Draw()
    {
        if (_hasPaint) DrawPolygon(Packed.Vector2s(_quad), Packed.Colors(_colors), Packed.Vector2s(_uv), _texture);
    }
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Material = null; CompositeMaterial.Dispose(); _texture.Dispose(); _world.Dispose();
    }
}
