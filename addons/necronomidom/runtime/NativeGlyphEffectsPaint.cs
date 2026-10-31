using System;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Glyph effects borrow the painter's cached native positions. Destroy this item before the shaper clears.
/// Fill, centered FreeType stroke and shadow masks share that run; blurred shadows retain one glyph-mask target
/// until this dirty-layout paint item is replaced. There is no per-frame shaping, mask render or allocation.</summary>
public partial class NativeGlyphEffectsPaint : Node2D, INativePaintBounds
{
    private readonly NativeShapedTextRun _shaped;
    private readonly TextEffectsRun _run;
    private readonly long _outline;
    private readonly SubViewport? _mask;
    private readonly World2D? _world;
    private readonly ViewportTexture? _texture;
    public Rect2 PaintBounds { get; }

    public NativeGlyphEffectsPaint(TextEffectsRun run, NativeShapedTextRun shaped)
    {
        _run = run; _shaped = shaped;
        if (!shaped.ShapedRid.IsValid) throw new ArgumentException("Glyph effects require a live shaped RID.", nameof(shaped));
        _outline = OutlineUnits(run.Effects.StrokeWidth);
        var (fill, stroke) = shaped.Ink(_outline, requireScalableFont: true);
        Vector2 baseline = Baseline(run.Text);
        Rect2 maskInk = Merge(fill, stroke);
        Rect2 visible = run.Text.Color.A > 0 ? fill : default;
        if (run.Effects.StrokeColor.A > 0) visible = Merge(visible, stroke);
        float support = 0;
        foreach (var shadow in run.Effects.Shadows.Span)
        {
            if (shadow.Color.A == 0 || !maskInk.HasArea()) continue;
            float fringe = BlurSupport(shadow.BlurRadius);
            var ink = maskInk.Grow(fringe);
            ink.Position += new Vector2(Float(shadow.OffsetX), Float(shadow.OffsetY));
            visible = Merge(visible, ink);
            if (shadow.BlurRadius > 0) support = Math.Max(support, fringe);
        }
        PaintBounds = visible.HasArea() ? new Rect2(visible.Position + baseline, visible.Size) : default;
        Rect2 maskBounds = default;
        if (support > 0 && maskInk.HasArea())
        {
            var padded = maskInk.Grow(support + 1);
            padded.Position += baseline;
            Vector2 origin = new(MathF.Floor(padded.Position.X), MathF.Floor(padded.Position.Y));
            Vector2 end = new(MathF.Ceiling(padded.End.X), MathF.Ceiling(padded.End.Y));
            maskBounds = new(origin, end - origin);
            _world = new World2D();
            _mask = new SubViewport
            {
                Size = new Vector2I(checked((int)maskBounds.Size.X), checked((int)maskBounds.Size.Y)),
                TransparentBg = true, Disable3D = true, GuiDisableInput = true, World2D = _world,
                HandleInputLocally = false, RenderTargetClearMode = SubViewport.ClearMode.Always,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
            };
            AddChild(_mask);
            var glyphMask = new NativeGlyphMaskPaint(shaped, baseline - origin, _outline);
            _mask.AddChild(glyphMask);
            _texture = _mask.GetTexture();
        }
        // CSS's first shadow is nearest the viewer. Every shadow stays behind the fill/stroke.
        ViewportTexture?[]? blurred = _texture == null ? null : new ViewportTexture?[run.Effects.Shadows.Length];
        for (int i = run.Effects.Shadows.Length - 1; i >= 0; i--)
        {
            var shadow = run.Effects.Shadows.Span[i];
            if (shadow.Color.A == 0) continue;
            ViewportTexture? source = _texture;
            if (shadow.BlurRadius > 0 && blurred != null)
            {
                source = null;
                for (int previous = i + 1; previous < blurred.Length; previous++)
                    if (run.Effects.Shadows.Span[previous].BlurRadius == shadow.BlurRadius && blurred[previous] != null)
                    { source = blurred[previous]; break; }
                if (source == null)
                {
                    var target = new NativeGlyphBlurTarget(_texture!, _mask!.Size, Float(shadow.BlurRadius / 2));
                    AddChild(target); source = target.Texture;
                }
                blurred[i] = source;
            }
            var paint = new NativeGlyphShadowPaint(shaped, run.Text, shadow, _outline, source, maskBounds)
            { ShowBehindParent = true };
            AddChild(paint);
        }
    }
    public override void _Ready()
    {
        // A child viewport texture is a real render dependency of the parent canvas/composite.
        if (_mask != null) _mask.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
    public override void _Draw()
    {
        Vector2 baseline = Baseline(_run.Text);
        if (_run.Text.Color.A > 0) _shaped.Draw(GetCanvasItem(), baseline, Color(_run.Text.Color));
        if (_outline > 0 && _run.Effects.StrokeColor.A > 0)
            _shaped.Draw(GetCanvasItem(), baseline, Color(_run.Effects.StrokeColor), _outline);
    }
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        _texture?.Dispose(); _world?.Dispose();
    }
    internal static long OutlineUnits(double cssWidth)
    {
        // Godot (upstream ed1daf0bf): the FreeType stroker's radius is outline_size / 4 pixels.
        // A CSS centered stroke has radius width / 2, so 1px/2px CSS strokes require 2/4.
        double units = cssWidth * 2;
        if (!double.IsFinite(units) || units < 0 || units != Math.Truncate(units) || units > int.MaxValue)
            throw new NotSupportedException("Native glyph stroke currently requires nonnegative half-CSS-pixel widths.");
        return (long)units;
    }
    internal static float BlurSupport(double radius) => Float(Math.Ceiling(radius * 1.5));
    internal static float Float(double value) => PolygonPaint.FiniteFloat(value);
    internal static Color Color(Rgba color) => new(color.R, color.G, color.B, color.A);
    internal static Vector2 Baseline(TextRun run) => new(Float(run.X), Float(run.Baseline));
    private static Rect2 Merge(Rect2 first, Rect2 second) => !first.HasArea() ? second : !second.HasArea() ? first : first.Merge(second);
}

public partial class NativeGlyphMaskPaint : Node2D
{
    private readonly NativeShapedTextRun _shaped;
    private readonly Vector2 _baseline;
    private readonly long _outline;
    internal NativeGlyphMaskPaint(NativeShapedTextRun shaped, Vector2 baseline, long outline)
    { _shaped = shaped; _baseline = baseline; _outline = outline; }
    public override void _Draw()
    {
        _shaped.Draw(GetCanvasItem(), _baseline, Colors.White);
        if (_outline > 0) _shaped.Draw(GetCanvasItem(), _baseline, Colors.White, _outline);
    }
}

public partial class NativeGlyphShadowPaint : Node2D
{
    private readonly NativeShapedTextRun _shaped;
    private readonly Vector2 _baseline;
    private readonly TextShadow _shadow;
    private readonly long _outline;
    private readonly ViewportTexture? _texture;
    private readonly Rect2 _maskBounds;
    private readonly ShaderMaterial? _material;
    internal NativeGlyphShadowPaint(NativeShapedTextRun shaped, TextRun text, TextShadow shadow, long outline,
        ViewportTexture? texture, Rect2 maskBounds)
    {
        _shaped = shaped; _shadow = shadow; _outline = outline; _texture = texture;
        Vector2 offset = new(NativeGlyphEffectsPaint.Float(shadow.OffsetX), NativeGlyphEffectsPaint.Float(shadow.OffsetY));
        _baseline = NativeGlyphEffectsPaint.Baseline(text) + offset;
        _maskBounds = new Rect2(maskBounds.Position + offset, maskBounds.Size);
        if (shadow.BlurRadius > 0 && texture != null)
        {
            _material = GlyphBlurMaterial.Create(NativeGlyphEffectsPaint.Float(shadow.BlurRadius / 2),
                new Vector2(1 / maskBounds.Size.X, 1 / maskBounds.Size.Y), Vector2.Down,
                NativeGlyphEffectsPaint.Color(shadow.Color));
            Material = _material;
        }
    }
    public override void _Draw()
    {
        if (_shadow.Color.A == 0) return;
        if (_shadow.BlurRadius > 0)
        {
            if (_texture != null) DrawTextureRect(_texture, _maskBounds, false);
            return;
        }
        var color = NativeGlyphEffectsPaint.Color(_shadow.Color);
        _shaped.Draw(GetCanvasItem(), _baseline, color);
        if (_outline > 0) _shaped.Draw(GetCanvasItem(), _baseline, color, _outline);
    }
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Material = null; _material?.Dispose();
    }
}
