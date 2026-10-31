#nullable enable
using Godot;

namespace NecronomiDom.Runtime;

public readonly record struct NativeCanvasAffine(float A, float B, float C, float D, float X, float Y)
{
    internal static NativeCanvasAffine From(Transform2D value) => new(value.X.X, value.X.Y, value.Y.X, value.Y.Y, value.Origin.X, value.Origin.Y);
}
public readonly record struct DocumentGeometrySnapshot(bool GuiSnapControlsToPixels,
    bool Snap2DTransformsToPixel, bool Snap2DVerticesToPixel, NativeCanvasAffine HostCanvas,
    NativeCanvasAffine HostDevice, NativeCanvasAffine PaintDevice, float Width, float Height);

public partial class DocumentHost
{
    /// <summary>Read-only native flags and input/paint transforms. Pixel snapping must be checked against actual rendered pixels.</summary>
    public DocumentGeometrySnapshot CaptureGeometrySnapshot()
    {
        var viewport = GetViewport();
        return new DocumentGeometrySnapshot(viewport.Get("gui_snap_controls_to_pixels").AsBool(),
            viewport.Get("snap_2d_transforms_to_pixel").AsBool(), viewport.Get("snap_2d_vertices_to_pixel").AsBool(),
            NativeCanvasAffine.From(GetGlobalTransformWithCanvas()),
            NativeCanvasAffine.From(GetViewportTransform() * GetGlobalTransform()),
            NativeCanvasAffine.From(_paint.GetViewportTransform() * _paint.GetGlobalTransform()), Size.X, Size.Y);
    }
}
