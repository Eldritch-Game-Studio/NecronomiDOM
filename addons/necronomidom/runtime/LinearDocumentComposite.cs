#nullable enable
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>Linear-light source-over, including the already rendered scene. Input remains on DocumentHost.</summary>
[GodotClass]
internal partial class LinearDocumentComposite : PolygonComposite
{
    internal LinearDocumentComposite()
    {
        LinearTarget = true;
        CompositeMaterial.Shader = WebPaintShaders.LinearDocument;
    }

    protected override void ConfigureDeviceCoverage()
        => CompositeMaterial.SetShaderParameter("destination_linear", GetViewport().UseHdr2D);
}
