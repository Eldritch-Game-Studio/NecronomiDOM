using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

[TestFixture]
public sealed class EllipticalPaddingContextTests
{
    private static PositionedPaintContext Context(string corners, int padding)
    {
        var document = HtmlImporter.Import("<!doctype html><div style='width:100px;height:80px;padding:" + padding
            + "px;border-style:solid;border-width:4px 8px 6px 2px;overflow:hidden;border-radius:" + corners
            + "'></div>", "ellipse-context", WebUrl.Parse("res:///ellipse-context.html")!).Document;
        var style = new StyleEngine(document).GetComputedValues(document.QuerySelector("div")!);
        var containing = new PositionedContainingBlock(new Rect(0, 0, 400, 300), new Rect(0, 0, 400, 300), true);
        var box = PositionedLayoutSolver.Complete(PositionedLayoutSolver.Prepare(style, containing, containing, 0, 0, default), 0);
        return PositionedLayoutSolver.ResolvePaintContext(style, box);
    }
    [Test]
    public void FinalPaddingEdgeSubtractsBordersNotCssPaddingAndKeepsEightIndependentRadii()
    {
        var context = Context("20px 30px 40px 10px / 10px 15px 20px 12px", 4);
        Assert.That(context.ClipBounds, Is.EqualTo(new Rect(2, 4, 108, 88)));
        Assert.That(context.ClipRadii, Is.EqualTo(new BorderRadii(new(18, 6), new(22, 11), new(32, 14), new(8, 6))));
        Assert.That(context.ClipRadius, Is.Zero, "the scalar fast path must not replace independent ellipses");
    }
    [Test]
    public void PercentCornersResolveAgainstFinalBorderDimensionsBeforeActualBorderInsets()
    {
        var context = Context("50% / 25%", 4);
        Assert.That(context.ClipRadii, Is.EqualTo(new BorderRadii(new(57, 20.5), new(51, 20.5), new(51, 18.5), new(57, 18.5))));
    }
    [Test]
    public void OneZeroInnerComponentRetainsSquareCornerLawWithoutInventedCircularRadius()
    {
        var context = Context("1px 20px 20px 20px / 10px 20px 20px 20px", 0);
        Assert.That(context.ClipRadii.TopLeft, Is.EqualTo(new BorderCorner(0, 6)));
        Assert.That(context.ClipRadius, Is.Zero);
    }
}
