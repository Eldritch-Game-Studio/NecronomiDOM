#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Retains isolated background clips and per-image immutable palette resources, not shared draw uniforms.</summary>
public sealed class NativeGradientPaintStore : IDisposable
{
    private sealed class Slot(BeginEllipticalClipRun clip, bool linear)
    {
        internal BeginEllipticalClipRun Clip = clip;
        internal readonly EllipticalClipGroup Group = new(clip);
        internal readonly NativeGradientPaint Paint = new(linear);
    }
    private readonly List<Slot> _slots = new();
    private int _cursor;
    public bool LinearComposition { get; init; }
    public void BeginRebuild() => _cursor = 0;
    public EllipticalClipGroup Create(GradientRun run)
    {
        Slot slot;
        if (_cursor == _slots.Count)
        {
            slot = new Slot(new BeginEllipticalClipRun(run.Clip.Bounds, run.Clip.Radii), LinearComposition);
            slot.Group.Content.AddChild(slot.Paint); _slots.Add(slot);
        }
        else slot = _slots[_cursor];
        _cursor++;
        if (slot.Clip.Geometry != run.Clip)
        {
            slot.Clip = new BeginEllipticalClipRun(run.Clip.Bounds, run.Clip.Radii);
            slot.Group.Update(slot.Clip);
        }
        slot.Group.BeginRebuild(); slot.Paint.Update(run);
        return slot.Group;
    }
    public void DetachBeforeClear()
    {
        foreach (var slot in _slots) slot.Group.GetParent()?.RemoveChild(slot.Group);
    }
    public void EndRebuild()
    {
        for (int i = _slots.Count - 1; i >= _cursor; i--) _slots[i].Group.Free();
        if (_cursor < _slots.Count) _slots.RemoveRange(_cursor, _slots.Count - _cursor);
    }
    public void Dispose()
    {
        DetachBeforeClear(); foreach (var slot in _slots) slot.Group.Free(); _slots.Clear(); _cursor = 0;
    }
}

/// <summary>
/// Analytic geometry, repeats, hints and hard stops on the public canvas shader path.
/// Color conversion reuses pure CSS interpolation via a segment-local float lookup palette.
/// The palette samples only color progress, never spatial pixels, and is uploaded only when colors change.
/// </summary>
[GodotClass]
public partial class NativeGradientPaint : Node2D, INativePaintBounds
{
    private const int Intervals = 2048;
    private static readonly StringName KindParameter = "gradient_kind", RepeatingParameter = "gradient_repeating",
        CircleParameter = "radial_circle", OriginAxisParameter = "gradient_origin_axis", LengthParameter = "gradient_length",
        TileParameter = "tile_rect", StepParameter = "tile_step", RepeatParameter = "tile_repeat",
        PaletteParameter = "palette", StopCountParameter = "stop_count", AverageParameter = "average_color";
    private readonly ShaderMaterial _material;
    private ImageTexture? _palette;
    private GradientImage? _paletteImage;
    private readonly bool _linear;
    public GradientRun Run { get; private set; } = null!;
    private bool OwnsRectEdge => Run.Clip.Radii == default && Run.Tile == Run.Clip.Bounds;
    public int PaletteUploadCount { get; private set; }
    public Rect2 PaintBounds
    {
        get
        {
            var clip = Run.Clip.Bounds; var tile = Run.Tile;
            double left = Run.RepeatX ? clip.X : Math.Max(clip.X, tile.X);
            double top = Run.RepeatY ? clip.Y : Math.Max(clip.Y, tile.Y);
            double right = Run.RepeatX ? clip.X + clip.Width : Math.Min(clip.X + clip.Width, tile.X + tile.Width);
            double bottom = Run.RepeatY ? clip.Y + clip.Height : Math.Min(clip.Y + clip.Height, tile.Y + tile.Height);
            var bounds = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
            if (OwnsRectEdge) bounds = ImageLayout.RasterBounds(bounds);
            return new((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
        }
    }

    internal NativeGradientPaint(bool linear = false)
    {
        _linear = linear;
        _material = new ShaderMaterial { Shader = WebPaintShaders.Gradient };
        Material = _material;
    }
    internal void Update(GradientRun run)
    {
        var g = run.Image;
        if (_paletteImage == null || !SameColors(_paletteImage, g)) UploadPalette(g);
        Run = run;
        _material.SetShaderParameter(KindParameter, (int)g.Kind);
        _material.SetShaderParameter(RepeatingParameter, g.Repeating);
        _material.SetShaderParameter(CircleParameter, g.Circular);
        _material.SetShaderParameter(OriginAxisParameter, new Vector4((float)g.X, (float)g.Y, (float)g.AxisX, (float)g.AxisY));
        _material.SetShaderParameter(LengthParameter, (float)g.Length);
        _material.SetShaderParameter(TileParameter, new Vector4((float)run.Tile.X, (float)run.Tile.Y, (float)run.Tile.Width, (float)run.Tile.Height));
        _material.SetShaderParameter(StepParameter, new Vector2((float)run.StepX, (float)run.StepY));
        _material.SetShaderParameter(RepeatParameter, new Vector2(run.RepeatX ? 1 : 0, run.RepeatY ? 1 : 0));
        _material.SetShaderParameter("gradient_edge_fringe", OwnsRectEdge);
        QueueRedraw();
    }
    private static bool SameColors(GradientImage a, GradientImage b)
    {
        if (a.InterpolationSpace != b.InterpolationSpace || a.HueMethod != b.HueMethod || a.Stops.Count != b.Stops.Count) return false;
        for (int i = 0; i < a.Stops.Count; i++) if (a.Stops[i] != b.Stops[i]) return false;
        return true;
    }
    private void UploadPalette(GradientImage image)
    {
        int intervals = !_linear && image.InterpolationSpace == "srgb" ? 1 : Intervals;
        using var data = Image.CreateEmpty(intervals + 2, image.Stops.Count, false, Image.Format.Rgbaf);
        for (int row = 0; row < image.Stops.Count; row++)
        {
            var stop = image.Stops[row];
            double width = row + 1 < image.Stops.Count ? image.Stops[row + 1].Position - stop.Position : 0;
            float hint = stop.HintAfter is { } h && width > 0 ? (float)((h - stop.Position) / width) : .5f;
            data.SetPixel(0, row, new Color((float)stop.Position, hint, 0, 0));
            var constant = GradientSampling.Segment(image, row, 0);
            bool solid = row + 1 == image.Stops.Count;
            for (int i = 0; i <= intervals; i++)
            {
                var c = solid || i == 0 ? constant : GradientSampling.Segment(image, row, (double)i / intervals);
                data.SetPixel(i + 1, row, Premultiply(c));
            }
        }
        if (_palette != null && _palette.GetWidth() == data.GetWidth() && _palette.GetHeight() == data.GetHeight())
            _palette.Update(data);
        else
        {
            var texture = ImageTexture.CreateFromImage(data);
            _material.SetShaderParameter(PaletteParameter, texture);
            _palette?.Dispose(); _palette = texture;
        }
        _material.SetShaderParameter(StopCountParameter, image.Stops.Count);
        var average = GradientSampling.Average(image);
        _material.SetShaderParameter(AverageParameter, Premultiply(average));
        _paletteImage = image; PaletteUploadCount++;
    }
    private Color Premultiply(Rgba value)
    {
        var c = new Color(value.R, value.G, value.B, value.A);
        if (_linear) c = c.SrgbToLinear();
        return new Color(c.R * c.A, c.G * c.A, c.B * c.A, c.A);
    }
    public override void _Draw() => DrawRect(PaintBounds, new Color(1, 1, 1, 1));
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Material = null; _material.Dispose(); _palette?.Dispose(); _palette = null;
    }
}
