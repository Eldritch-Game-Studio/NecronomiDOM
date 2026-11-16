#nullable enable
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>Linear-light source-over, including the already rendered scene. Input remains on DocumentHost.</summary>
[GodotClass]
internal partial class LinearDocumentComposite : PolygonComposite
{
    /// <summary>The scene-colour read for this composite. It copies only the area the composite draws, and nothing
    /// while it paints nothing: the shader samples the destination only inside its own quad.</summary>
    private readonly BackBufferCopy _sceneCopy;

    /// <summary><paramref name="sceneCopy"/> must be an identity-transform sibling drawn before this composite, so
    /// both share one local space and the copy completes before the composite samples it.</summary>
    internal LinearDocumentComposite(BackBufferCopy sceneCopy)
    {
        LinearTarget = true;
        CompositeMaterial.Shader = WebPaintShaders.LinearDocument;
        _sceneCopy = sceneCopy;
        _sceneCopy.CopyMode = BackBufferCopy.CopyModeEnum.Rect;
        _sceneCopy.Visible = false;
    }

    protected override void ConfigureDeviceCoverage()
        => CompositeMaterial.SetShaderParameter("destination_linear", GetViewport().UseHdr2D);

    protected override void DeviceCoverageChanged(Rect2 local, bool painted)
    {
        if (painted && _sceneCopy.Rect != local) _sceneCopy.Rect = local;
        if (_sceneCopy.Visible != painted) _sceneCopy.Visible = painted;
    }
}
