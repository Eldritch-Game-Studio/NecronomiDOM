#nullable enable
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;
public partial class ShapedTextPaint : Node2D, INativePaintBounds
{
    public AdvancedTextShaper Shaper { get; }
    public TextRun Run { get; }
    public Rect2 PaintBounds { get; }
    internal ShapedTextPaint(AdvancedTextShaper shaper, TextRun run)
    {
        Shaper = shaper; Run = run; PaintBounds = shaper.InkBounds(run);
    }
    public override void _Draw() => Shaper.Draw(GetCanvasItem(), Run);
}

public partial class NativeImagePaint : Node2D, INativePaintBounds
{
    private static readonly StringName RasterRectParameter = "image_raster_rect";
    private static readonly StringName SourceRectParameter = "image_source_rect";
    public ImageRun Run { get; set; } = null!;
    public Texture2D ImageTexture { get; set; } = null!;
    public Rect Source { get; set; }
    public Rect RasterDestination { get; set; }
    public Rect2 PaintBounds => new((float)RasterDestination.X, (float)RasterDestination.Y,
        (float)RasterDestination.Width, (float)RasterDestination.Height);
    public void ConfigureCoordinates()
    {
        var d = RasterDestination; var s = Source;
        SetInstanceShaderParameter(RasterRectParameter, new Vector4((float)d.X, (float)d.Y, (float)d.Width, (float)d.Height));
        SetInstanceShaderParameter(SourceRectParameter, new Vector4((float)s.X, (float)s.Y, (float)s.Width, (float)s.Height));
    }
    public override void _Draw()
    {
        var d = RasterDestination; var s = Source;
        DrawTextureRectRegion(ImageTexture, new Rect2((float)d.X, (float)d.Y, (float)d.Width, (float)d.Height),
            new Rect2((float)s.X, (float)s.Y, (float)s.Width, (float)s.Height), null, false, false);
    }
}
