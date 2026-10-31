using Godot;

namespace NecronomiDom.Runtime;

/// <summary>One horizontal Gaussian pass shared by shadows with the same radius on a shaped run.
/// The shadow canvas performs the vertical pass. Both are dirty-only child texture dependencies.</summary>
public partial class NativeGlyphBlurTarget : Node
{
    private readonly World2D _world = new();
    private readonly SubViewport _target;
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
        _target.AddChild(new NativeGlyphBlurPass(source, size, sigma));
        Texture = _target.GetTexture();
    }
    public override void _Ready() => _target.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Texture.Dispose(); _world.Dispose();
    }
}

public partial class NativeGlyphBlurPass : Node2D
{
    private readonly ViewportTexture _source;
    private readonly Rect2 _rect;
    private readonly ShaderMaterial _material;
    internal NativeGlyphBlurPass(ViewportTexture source, Vector2I size, float sigma)
    {
        _source = source; _rect = new Rect2(Vector2.Zero, size);
        _material = GlyphBlurMaterial.Create(sigma, new Vector2(1f / size.X, 1f / size.Y), Vector2.Right, Colors.White);
        Material = _material;
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
    private static readonly StringName SigmaParameter = "shadow_sigma";
    private static readonly StringName TexelParameter = "mask_texel_size";
    private static readonly StringName AxisParameter = "blur_axis";
    private static readonly StringName ColorParameter = "shadow_color";
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
