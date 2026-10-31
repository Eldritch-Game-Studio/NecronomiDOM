using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private static bool EllipticalOverflow(PositionedPaintContext context) => HasCorners(context.ClipRadii)
        && !UniformCircularCorners(context.ClipRadii, out _);

    private (PaintRun Begin, PaintRun End) OverflowPaintRuns(PositionedPaintContext context)
    {
        Rect bounds = EffectiveClipBounds(context);
        if (EllipticalOverflow(context))
            return (new BeginEllipticalClipRun(bounds, context.ClipRadii), new EndEllipticalClipRun(bounds));
        if (context.ClipRadius > 0)
            return (new BeginRoundedRectClipRun(bounds, context.ClipRadius), new EndRoundedRectClipRun());
        return (new BeginRectClipRun(bounds), new EndRectClipRun());
    }
}
