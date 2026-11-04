#nullable enable
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>Immutable unit quad; the node transform carries box size without regenerating primitive surfaces.</summary>
[GodotClass]
public partial class NativeBorderPaint : MeshInstance2D, INativePaintBounds
{
    public Rect2 PaintBounds => new(-0.5f, -0.5f, 1, 1);
}
