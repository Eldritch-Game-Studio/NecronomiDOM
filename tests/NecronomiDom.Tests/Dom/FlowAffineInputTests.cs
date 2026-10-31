using System;
using System.Linq;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    [Test]
    public void ActualAffinePaintAndHitUseTheSameFinalBorderOriginWithoutChangingFlow()
    {
        var (document, input, display) = Fixture("<div id=box></div><div id=after></div>", """
            #box{width:100px;height:40px;background-color:red;translate:20px 10px;scale:2;transform-origin:0 0}
            #after{width:100px;height:20px;background-color:blue}
            """);
        using var owner = input;
        Assert.That(Bounds(display, "box"), Is.EqualTo(new Rect(0, 0, 100, 40)));
        Assert.That(Bounds(display, "after").Y, Is.EqualTo(40));
        var box = display.Boxes.Single(b => b.Element.Id == "box");
        Assert.That(box.VisualBounds, Is.EqualTo(new Rect(20, 10, 200, 80)));
        Assert.That(display.Runs.OfType<BeginTransformRun>().Single().Transform, Is.EqualTo(new Affine2D(2, 0, 0, 2, 20, 10)));
        Assert.That(Click(document, input, 210, 30), Is.EqualTo("box"));
        Assert.That(Click(document, input, 10, 10), Is.Not.EqualTo("box"));
    }
    [Test]
    public void NestedOwnAffineComposesAncestorOnceForPaintInputAndVisualBounds()
    {
        var (document, input, display) = Fixture("<div id=parent><div id=child></div></div>", """
            #parent{width:100px;height:100px;translate:40px 30px;scale:2;transform-origin:0 0}
            #child{width:20px;height:10px;background-color:green;translate:10px 5px;scale:3;transform-origin:0 0}
            """);
        using var owner = input;
        var child = display.Boxes.Single(b => b.Element.Id == "child");
        Assert.That(child.VisualBounds, Is.EqualTo(new Rect(60, 40, 120, 60)));
        Assert.That(display.Runs.OfType<BeginTransformRun>().Count(), Is.EqualTo(2));
        Assert.That(Click(document, input, 170, 90), Is.EqualTo("child"));
        Assert.That(Click(document, input, 59, 40), Is.Not.EqualTo("child"));
    }
    [Test]
    public void IdentityAuthoredTransformEstablishesActualAbsoluteContainingBlock()
    {
        var (document, input, display) = Fixture("<div id=spacer></div><div id=parent><div id=child></div></div>", """
            #spacer{height:40px}#parent{width:100px;height:80px;transform:translate(0px)}
            #child{position:absolute;left:10px;top:15px;width:20px;height:20px;background-color:red}
            """);
        using var owner = input;
        Assert.That(Bounds(display, "child"), Is.EqualTo(new Rect(10, 55, 20, 20)));
        Assert.That(Click(document, input, 15, 60), Is.EqualTo("child"));
    }
    [Test]
    public void SingularAffineDoesNotLeaveAnUnpaintedClickableSurface()
    {
        var (document, input, display) = Fixture("<div id=box></div>", "#box{width:100px;height:100px;background-color:red;scale:0}");
        using var owner = input;
        Assert.That(Click(document, input, 50, 50), Is.Not.EqualTo("box"));
        Assert.That(display.Boxes.Single(b => b.Element.Id == "box").ContainsPoint(50, 50), Is.False);
    }
    [Test]
    public void TransformedChildDoesNotTransformItsAncestorsOverflowClip()
    {
        var (document, input, _) = Fixture("<div id=parent><div id=child></div></div>", """
            #parent{width:50px;height:50px;overflow:hidden;translate:20px 10px;background-color:blue}
            #child{width:50px;height:50px;background-color:green;translate:30px 0}
            """);
        using var owner = input;
        Assert.That(Click(document, input, 60, 20), Is.EqualTo("child"));
        Assert.That(Click(document, input, 80, 20), Is.Not.EqualTo("child"));
    }
    [Test]
    public void BottomAnchoredSubtreeConjugatesItsAlreadyResolvedChildOrigin()
    {
        var (document, input, display) = Fixture("<div id=parent><div id=absolute><div id=child></div></div></div>", """
            #parent{position:relative;width:100px;height:100px}
            #absolute{position:absolute;bottom:10px;width:40px}
            #child{width:40px;height:20px;background-color:red;scale:2;transform-origin:50% 50%}
            """);
        using var owner = input;
        var child = display.Boxes.Single(b => b.Element.Id == "child");
        Assert.That(child.Bounds, Is.EqualTo(new Rect(0, 70, 40, 20)));
        Assert.That(child.VisualBounds, Is.EqualTo(new Rect(-20, 60, 80, 40)));
        Assert.That(Click(document, input, 50, 80), Is.EqualTo("child"));
    }
    [Test]
    public void ThreeDimensionalTransformsRemainExplicitlyUnavailable()
    {
        Assert.That(() => Fixture("<div id=box></div>", "#box{width:20px;height:20px;transform:rotateX(30deg)}"),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("2D CSS transform"));
    }
}
