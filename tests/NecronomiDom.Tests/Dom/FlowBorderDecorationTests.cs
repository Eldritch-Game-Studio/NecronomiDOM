using System;
using System.Linq;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    [Test]
    public void ActualAsymmetricSidesEmitOneTypedBorderRunWithFinalUsedBounds()
    {
        var (_, input, display) = Fixture("<div id=box></div>", """
            #box{width:100px;height:80px;border-style:solid;border-width:4px 8px 6px 2px;
                border-color:red green blue black;background-color:white}
            """);
        using var owner = input;
        var run = display.Runs.OfType<BorderBoxRun>().Single();
        Assert.That(run.Bounds, Is.EqualTo(new Rect(0, 0, 110, 90)));
        Assert.That((run.Borders.Top.Width, run.Borders.Right.Width, run.Borders.Bottom.Width, run.Borders.Left.Width),
            Is.EqualTo((4d, 8d, 6d, 2d)));
        Assert.That(run.Borders.Top.Color, Is.EqualTo(new Rgba(1, 0, 0, 1)));
        Assert.That(run.Borders.Bottom.Color, Is.EqualTo(new Rgba(0, 0, 1, 1)));
    }
    [Test]
    public void UniformDecorationKeepsExistingMultiMeshBoxRun()
    {
        var (_, input, display) = Fixture("<div id=box></div>", "#box{width:100px;height:80px;border:2px solid red;border-radius:10px;background-color:blue}");
        using var owner = input;
        Assert.That(display.Runs.OfType<BorderBoxRun>(), Is.Empty);
        Assert.That(display.Runs.OfType<BoxRun>().Any(run => run.Bounds == new Rect(0, 0, 104, 84)
            && run.Style.BorderWidth == 2 && run.Style.Radius == 10), Is.True);
    }
    [Test]
    public void IndependentEllipticalCornersUseFinalBoxAndActualOuterHitShape()
    {
        var (document, input, display) = Fixture("<div id=box></div>", """
            #box{width:100px;height:80px;background-color:red;border-radius:20px 30px 40px 10px / 10px 15px 20px 5px}
            """);
        using var owner = input;
        var run = display.Runs.OfType<BorderBoxRun>().Single();
        Assert.That(run.Radii, Is.EqualTo(new BorderRadii(new(20, 10), new(30, 15), new(40, 20), new(10, 5))));
        Assert.That(Click(document, input, 1, 1), Is.Not.EqualTo("box"));
        Assert.That(Click(document, input, 4, 4), Is.EqualTo("box"));
        Assert.That(Click(document, input, 50, 40), Is.EqualTo("box"));
    }
    [Test]
    public void BorderRunFollowsActualBottomAnchorTranslation()
    {
        var (_, input, display) = Fixture("<div id=parent><div id=child></div></div>", """
            #parent{position:relative;width:100px;height:100px}
            #child{position:absolute;bottom:10px;width:20px;height:20px;border-style:solid;border-width:1px 2px 3px 4px;border-color:red}
            """);
        using var owner = input;
        Assert.That(display.Runs.OfType<BorderBoxRun>().Single().Bounds, Is.EqualTo(new Rect(0, 66, 26, 24)));
    }
    [Test]
    public void NonuniformPaddingEllipseEmitsActualTypedClipAndPreservesOuterBorderHitRing()
    {
        var (document, input, display) = Fixture("<div id=parent><div id=child></div></div>", """
            #parent{position:relative;width:100px;height:80px;border-style:solid;border-width:4px 8px 6px 2px;
                border-color:black;border-radius:40px 30px / 30px 40px;background-color:blue;overflow:hidden}
            #child{position:absolute;inset:0;background-color:green}
            """);
        using var owner = input;
        var clip = display.Runs.OfType<BeginEllipticalClipRun>().Single();
        Assert.That(clip.Bounds, Is.EqualTo(new Rect(2, 4, 100, 80)));
        Assert.That(clip.Radii.TopLeft, Is.EqualTo(new BorderCorner(38, 26)));
        Assert.That(clip.Radii.TopRight, Is.EqualTo(new BorderCorner(22, 36)));
        Assert.That(display.Runs.OfType<EndEllipticalClipRun>().Single().Bounds, Is.EqualTo(clip.Bounds));
        Assert.That(Click(document, input, 20, 6), Is.EqualTo("parent"), "outer painted border is not clipped by its own padding edge");
        Assert.That(Click(document, input, 40, 30), Is.EqualTo("child"));
        Assert.That(Click(document, input, 3, 5), Is.Not.EqualTo("child"));
    }
    [Test]
    public void DeferredBottomTranslationMovesBothEllipticalClipBoundsWithoutChangingRadii()
    {
        var (_, input, display) = Fixture("<div id=parent><div id=clip><div id=fill></div></div></div>", """
            #parent{position:relative;width:120px;height:150px}
            #clip{position:absolute;bottom:10px;width:100px;height:80px;border-style:solid;
                border-width:4px 8px 6px 2px;border-radius:40px 30px / 30px 40px;overflow:hidden}
            #fill{width:100px;height:80px;background-color:green}
            """);
        using var owner = input;
        var begin = display.Runs.OfType<BeginEllipticalClipRun>().Single();
        Assert.That(begin.Bounds, Is.EqualTo(new Rect(2, 54, 100, 80)));
        Assert.That(display.Runs.OfType<EndEllipticalClipRun>().Single().Bounds, Is.EqualTo(begin.Bounds));
        Assert.That(begin.Radii.TopLeft, Is.EqualTo(new BorderCorner(38, 26)));
    }
    [Test]
    public void DescendantEllipticalClipUsesItsOwnersInverseTransformExactlyOnce()
    {
        var (document, input, _) = Fixture("<div id=clip><div id=fill></div></div>", """
            #clip{position:relative;width:100px;height:80px;border-style:solid;border-width:4px 8px 6px 2px;
                border-color:black;border-radius:40px 30px / 30px 40px;overflow:hidden;transform:translate(30px,20px)}
            #fill{position:absolute;inset:0;background-color:green}
            """);
        using var owner = input;
        Assert.That(Click(document, input, 33, 25), Is.Not.EqualTo("fill"));
        Assert.That(Click(document, input, 70, 50), Is.EqualTo("fill"));
    }
}
