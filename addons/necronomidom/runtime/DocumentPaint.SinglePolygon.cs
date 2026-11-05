#nullable enable
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentPaint
{
    // Preserve default sRGB target/blend precision. The opt-in linear document
    // uses HDR isolation and can fuse an identical triangle/quad fill and mask.
    // Logical display-list structure and hit-testing remain unchanged.
    private bool TrySinglePolygon(DisplayList list, int index, CanvasItem parent, out PolygonRun polygon)
    {
        if (LinearComposition && index + 2 < list.Runs.Count &&
            list.Runs[index] is BeginPolygonClipRun clip &&
            list.Runs[index + 1] is PolygonRun fill &&
            list.Runs[index + 2] is EndPolygonClipRun &&
            fill.Geometry.Count <= 4 && clip.Bounds == fill.Bounds && clip.Geometry.Equals(fill.Geometry) &&
            parent.GetViewport().Msaa2D == Viewport.Msaa.Disabled)
        {
            polygon = fill;
            return true;
        }
        polygon = null!;
        return false;
    }
}
