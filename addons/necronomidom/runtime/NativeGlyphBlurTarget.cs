#nullable enable
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>One horizontal Gaussian pass shared by shadows with the same radius on a shaped run.
/// The shadow canvas performs the vertical pass. Both are dirty-only child texture dependencies.</summary>
[GodotClass]
public partial class NativeGlyphBlurTarget : Node
{
    private readonly World2D _world = new();
    private readonly SubViewport _target;
    private readonly NativeGlyphBlurPass _pass;
    private Vector2I _size;
    public ViewportTexture Texture { get; }
    internal NativeGlyphBlurTarget(ViewportTexture source, Vector2I size, float sigma)
    {
        _target = new SubViewport
        {
            Size = size, TransparentBg = true, Disable3D = true, GuiDisableInput = true,
            World2D = _world, HandleInputLocally = false,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        AddChild(_target);
        _size = size;
        _pass = new NativeGlyphBlurPass(source, size, sigma);
        _target.AddChild(_pass);
        Texture = _target.GetTexture();
    }
    public override void _Ready() => _target.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    internal void Update(Vector2I size, float sigma)
    {
        if (_size != size) { _size = size; _target.Size = size; }
        _pass.Update(size, sigma);
        _target.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
    internal void Disable() => _target.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Texture.Dispose(); _world.Dispose();
    }
}

[GodotClass]
public partial class NativeGlyphBlurPass : Node2D
{
    private readonly ViewportTexture _source;
    private Rect2 _rect;
    private float _sigma;
    private readonly ShaderMaterial _material;
    internal NativeGlyphBlurPass(ViewportTexture source, Vector2I size, float sigma)
    {
        _source = source; _rect = new Rect2(Vector2.Zero, size); _sigma = sigma;
        _material = GlyphBlurMaterial.Create(sigma, new Vector2(1f / size.X, 1f / size.Y), Vector2.Right, new Color(1, 1, 1, 1));
        Material = _material;
    }
    internal void Update(Vector2I size, float sigma)
    {
        var extent = new Vector2(size.X, size.Y);
        if (_rect.Size != extent)
        {
            _rect.Size = extent;
            _material.SetShaderParameter(GlyphBlurMaterial.TexelParameter, new Vector2(1f / size.X, 1f / size.Y));
            QueueRedraw();
        }
        if (_sigma != sigma)
        {
            _sigma = sigma;
            _material.SetShaderParameter(GlyphBlurMaterial.SigmaParameter, sigma);
        }
    }
    public override void _Draw() => DrawTextureRect(_source, _rect, false);
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Material = null; _material.Dispose();
    }
}

internal static class GlyphBlurMaterial
{
    internal static readonly StringName SigmaParameter = "shadow_sigma";
    internal static readonly StringName TexelParameter = "mask_texel_size";
    private static readonly StringName AxisParameter = "blur_axis";
    internal static readonly StringName ColorParameter = "shadow_color";
    internal static ShaderMaterial Create(float sigma, Vector2 texel, Vector2 axis, Color color)
    {
        var material = new ShaderMaterial { Shader = WebPaintShaders.GlyphShadow };
        material.SetShaderParameter(SigmaParameter, sigma);
        material.SetShaderParameter(TexelParameter, texel);
        material.SetShaderParameter(AxisParameter, axis);
        material.SetShaderParameter(ColorParameter, color);
        return material;
    }
}
