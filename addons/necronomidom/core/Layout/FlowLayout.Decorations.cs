using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private static bool UniformBorder(BorderSides sides)
    {
        if (sides.Top.Width == 0 && sides.Right.Width == 0 && sides.Bottom.Width == 0 && sides.Left.Width == 0) return true;
        return sides.Top == sides.Right && sides.Top == sides.Bottom && sides.Top == sides.Left;
    }
    private static bool UniformCircularCorners(BorderRadii radii, out double radius)
    {
        var corner = radii.TopLeft;
        radius = corner.X;
        return corner.X == corner.Y && corner == radii.TopRight && corner == radii.BottomRight && corner == radii.BottomLeft;
    }
    private static bool HasCorners(BorderRadii radii) => HasCorner(radii.TopLeft) || HasCorner(radii.TopRight)
        || HasCorner(radii.BottomRight) || HasCorner(radii.BottomLeft);
    private static bool HasCorner(BorderCorner corner) => corner.X > 0 && corner.Y > 0;
    private static bool HasBorder(BorderSides sides) => sides.Top.Width > 0 || sides.Right.Width > 0 || sides.Bottom.Width > 0 || sides.Left.Width > 0;
}
