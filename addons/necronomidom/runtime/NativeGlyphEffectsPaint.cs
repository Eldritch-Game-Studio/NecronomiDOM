#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Glyph effects borrow the painter's cached native positions. Destroy this item before the shaper clears.
/// Fill, stroke and shadows share the run; changed text reuses bounded mask/blur targets rather than
/// allocating viewports and material bindings on each retained paint update.</summary>
[GodotClass]
public partial class NativeGlyphEffectsPaint : Node2D, INativePaintBounds
{
    private NativeShapedTextRun _shaped;
    private TextEffectsRun _run = null!;
    private long _outline;
    private SubViewport? _mask;
    private NativeGlyphMaskPaint? _glyphMask;
    private World2D? _world;
    private ViewportTexture? _texture;
    private Vector2I _capacity;
    private readonly List<NativeGlyphShadowPaint> _shadows = new();
    private readonly List<NativeGlyphBlurTarget> _blurs = new();
    private readonly List<float> _blurSigmas = new();
    public Rect2 PaintBounds { get; private set; }

    public NativeGlyphEffectsPaint(TextEffectsRun run, NativeShapedTextRun shaped) => Update(run, shaped);
    internal void Update(TextEffectsRun run, NativeShapedTextRun shaped)
    {
        if (!shaped.ShapedRid.IsValid) throw new ArgumentException("Glyph effects require a live shaped RID.", nameof(shaped));
        _run = run; _shaped = shaped; _outline = OutlineUnits(run.Effects.StrokeWidth);
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
        if (_mask != null) _mask.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        foreach (var blur in _blurs) blur.Disable();
        _blurSigmas.Clear();
        Rect2 maskBounds = default;
        if (support > 0 && maskInk.HasArea())
        {
            var padded = maskInk.Grow(support + 1);
            padded.Position += baseline;
            Vector2 origin = new(MathF.Floor(padded.Position.X), MathF.Floor(padded.Position.Y));
            Vector2 end = new(MathF.Ceiling(padded.End.X), MathF.Ceiling(padded.End.Y));
            maskBounds = new(origin, end - origin);
            var required = new Vector2I(checked(((int)maskBounds.Size.X + 15) / 16 * 16),
                checked(((int)maskBounds.Size.Y + 15) / 16 * 16));
            if (_mask == null)
            {
                _capacity = required; _world = new World2D();
                _mask = new SubViewport
                {
                    Size = _capacity, TransparentBg = true, Disable3D = true, GuiDisableInput = true, World2D = _world,
                    HandleInputLocally = false, RenderTargetClearMode = SubViewport.ClearMode.Always,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
                };
                AddChild(_mask);
                _glyphMask = new NativeGlyphMaskPaint(shaped, baseline - origin, _outline);
                _mask.AddChild(_glyphMask);
                _texture = _mask.GetTexture();
            }
            else
            {
                var capacity = new Vector2I(Math.Max(_capacity.X, required.X), Math.Max(_capacity.Y, required.Y));
                if (capacity != _capacity) { _capacity = capacity; _mask.Size = capacity; }
                _glyphMask!.Update(shaped, baseline - origin, _outline);
            }
            _mask.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
        int count = run.Effects.Shadows.Length;
        bool orderChanged = _shadows.Count < count;
        while (_shadows.Count < count)
        {
            var shadow = new NativeGlyphShadowPaint { ShowBehindParent = true };
            AddChild(shadow); _shadows.Add(shadow);
        }
        for (int i = _shadows.Count - 1; i >= count; i--) _shadows[i].Visible = false;
        // CSS's first shadow is nearest the viewer. Reuse each current distinct radius by slot,
        // not by a permanent radius-keyed cache that could grow across animated styles.
        for (int i = count - 1; i >= 0; i--)
        {
            var shadow = run.Effects.Shadows.Span[i];
            var paint = _shadows[i]; paint.Visible = shadow.Color.A > 0;
            ViewportTexture? source = null;
            if (shadow.Color.A > 0 && shadow.BlurRadius > 0 && _texture != null && maskBounds.HasArea())
            {
                float sigma = Float(shadow.BlurRadius / 2);
                int slot = _blurSigmas.IndexOf(sigma);
                if (slot < 0)
                {
                    slot = _blurSigmas.Count; _blurSigmas.Add(sigma);
                    if (slot == _blurs.Count)
                    {
                        var blur = new NativeGlyphBlurTarget(_texture, _capacity, sigma);
                        AddChild(blur); _blurs.Add(blur);
                    }
                    _blurs[slot].Update(_capacity, sigma);
                }
                source = _blurs[slot].Texture;
            }
            paint.Update(shaped, run.Text, shadow, _outline, source, maskBounds, _capacity);
            if (orderChanged) MoveChild(paint, GetChildCount() - 1);
        }
        QueueRedraw();
    }
    public override void _Ready()
    {
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

[GodotClass]
public partial class NativeGlyphMaskPaint : Node2D
{
    private NativeShapedTextRun _shaped;
    private Vector2 _baseline;
    private long _outline;
    internal NativeGlyphMaskPaint(NativeShapedTextRun shaped, Vector2 baseline, long outline)
    { _shaped = shaped; _baseline = baseline; _outline = outline; }
    internal void Update(NativeShapedTextRun shaped, Vector2 baseline, long outline)
    { _shaped = shaped; _baseline = baseline; _outline = outline; QueueRedraw(); }
    public override void _Draw()
    {
        _shaped.Draw(GetCanvasItem(), _baseline, new Color(1, 1, 1, 1));
        if (_outline > 0) _shaped.Draw(GetCanvasItem(), _baseline, new Color(1, 1, 1, 1), _outline);
    }
}

[GodotClass]
public partial class NativeGlyphShadowPaint : Node2D
{
    private NativeShapedTextRun _shaped;
    private Vector2 _baseline;
    private TextShadow _shadow;
    private long _outline;
    private ViewportTexture? _texture;
    private Rect2 _maskBounds, _sourceRect;
    private ShaderMaterial? _material;
    private bool _usesBlurMaterial;
    private float _sigma;
    private Vector2 _texel;
    private Color _color;
    internal void Update(NativeShapedTextRun shaped, TextRun text, TextShadow shadow, long outline,
        ViewportTexture? texture, Rect2 maskBounds, Vector2I capacity)
    {
        _shaped = shaped; _shadow = shadow; _outline = outline; _texture = texture;
        Vector2 offset = new(NativeGlyphEffectsPaint.Float(shadow.OffsetX), NativeGlyphEffectsPaint.Float(shadow.OffsetY));
        _baseline = NativeGlyphEffectsPaint.Baseline(text) + offset;
        _maskBounds = new Rect2(maskBounds.Position + offset, maskBounds.Size);
        _sourceRect = new Rect2(Vector2.Zero, maskBounds.Size);
        bool blurred = shadow.BlurRadius > 0 && texture != null;
        if (blurred)
        {
            float sigma = NativeGlyphEffectsPaint.Float(shadow.BlurRadius / 2);
            Vector2 texel = new(1f / capacity.X, 1f / capacity.Y);
            Color color = NativeGlyphEffectsPaint.Color(shadow.Color);
            if (_material == null)
                _material = GlyphBlurMaterial.Create(sigma, texel, Vector2.Down, color);
            else
            {
                if (_sigma != sigma) _material.SetShaderParameter(GlyphBlurMaterial.SigmaParameter, sigma);
                if (_texel != texel) _material.SetShaderParameter(GlyphBlurMaterial.TexelParameter, texel);
                if (_color != color) _material.SetShaderParameter(GlyphBlurMaterial.ColorParameter, color);
            }
            _sigma = sigma; _texel = texel; _color = color;
        }
        if (_usesBlurMaterial != blurred) { Material = blurred ? _material : null; _usesBlurMaterial = blurred; }
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_shadow.Color.A == 0) return;
        if (_shadow.BlurRadius > 0)
        {
            if (_texture != null) DrawTextureRectRegion(_texture, _maskBounds, _sourceRect);
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
