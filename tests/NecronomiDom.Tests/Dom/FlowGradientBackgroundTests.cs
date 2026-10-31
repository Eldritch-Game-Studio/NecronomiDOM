using System;
using System.Linq;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    private GradientRun Gradient(string css)
    {
        var (_, input, display) = Fixture("<div id=tile></div>", "#tile{width:100px;height:80px;" + css + "}");
        using var owner = input;
        return display.Runs.OfType<GradientRun>().Single();
    }
    [TestCase("0deg", 0, -1, 80)]
    [TestCase("90deg", 1, 0, 100)]
    [TestCase("180deg", 0, 1, 80)]
    [TestCase("270deg", -1, 0, 100)]
    [TestCase("-90deg", -1, 0, 100)]
    [TestCase("450deg", 1, 0, 100)]
    [TestCase("3600000090deg", 1, 0, 100)]
    [TestCase("calc(.25turn + 0deg)", 1, 0, 100)]
    public void TypedLinearAnglesHaveCssClockwiseGeometry(string angle, double x, double y, double length)
    {
        var g = Gradient("background-image:linear-gradient(" + angle + ",red,blue)").Image;
        Assert.That(g.AxisX, Is.EqualTo(x).Within(1e-12)); Assert.That(g.AxisY, Is.EqualTo(y).Within(1e-12));
        Assert.That(g.Length, Is.EqualTo(length).Within(1e-12));
    }
    [Test]
    public void ComputedEllipsePercentagesUseIndependentAxesAndOutsideCenter()
    {
        var g = Gradient("background-image:radial-gradient(ellipse 40% 25% at -20% 130%,red,blue)").Image;
        Assert.That((g.X, g.Y, g.AxisX, g.AxisY), Is.EqualTo((-20d,104d,40d,20d)));
    }
    [Test]
    public void ComputedEdgeOffsetsAndClosestCornerEllipseSharePositionResolver()
    {
        var g = Gradient("background-image:radial-gradient(closest-corner at right 10px bottom 12px,red,blue)").Image;
        Assert.That((g.X,g.Y), Is.EqualTo((90d,68d)));
        Assert.That(g.AxisX, Is.EqualTo(10 * Math.Sqrt(2)).Within(1e-12));
        Assert.That(g.AxisY, Is.EqualTo(12 * Math.Sqrt(2)).Within(1e-12));
    }
    [Test]
    public void ZeroPeriodRepeatingGradientUsesPremultipliedAverageNotLastStop()
    {
        var g = Gradient("background-image:repeating-linear-gradient(red 0px,blue 0px)").Image;
        Assert.That(GradientSampling.Sample(g, 10), Is.EqualTo(new Rgba(.5f,0,.5f,1)));
    }
    [Test]
    public void MixedUrlAndGeneratedLayersReuseTheExistingImageResourceAdapter()
    {
        var resources = new BackgroundResources();
        var (_, input, display) = Fixture("<div id=tile></div>", "#tile{width:100px;height:80px;"
            + "background-image:linear-gradient(red,transparent),url(images/icon.png);background-repeat:no-repeat}", resources);
        using var owner = input;
        var image = display.Runs.OfType<BackgroundImageRun>().Single();
        var gradient = display.Runs.OfType<GradientRun>().Single();
        Assert.That(display.Runs.IndexOf(image), Is.LessThan(display.Runs.IndexOf(gradient)));
        Assert.That(resources.Loads, Is.EqualTo(1)); Assert.That(resources.OwnerTag, Is.EqualTo("div"));
    }
    [Test]
    public void StopFixupExpandsHardStopsClampsDescendingAndDistributesOmittedPositions()
    {
        var g = Gradient("background-image:linear-gradient(90deg,red 20%,green 10%,blue,white 80% 90%,black)").Image;
        Assert.That(g.Stops.Select(s => s.Position), Is.EqualTo(new[] { .2, .2, .5, .8, .9, 1 }));
        Assert.That(GradientSampling.Sample(g, .2).B, Is.EqualTo(0));
    }
    [Test]
    public void TransitionHintIsNotTurnedIntoAColorStop()
    {
        var g = Gradient("background-image:linear-gradient(90deg,red,25%,blue)").Image;
        Assert.That(g.Stops.Count, Is.EqualTo(2)); Assert.That(g.Stops[0].HintAfter, Is.EqualTo(.25));
        var middle = GradientSampling.Sample(g, .25);
        Assert.That(middle.R, Is.EqualTo(.5).Within(1e-6)); Assert.That(middle.B, Is.EqualTo(.5).Within(1e-6));
    }
    [Test]
    public void AlphaInterpolationIsPremultipliedAndCurrentcolorIsComputed()
    {
        var g = Gradient("color:blue;background-image:linear-gradient(90deg,rgba(255,0,0,0),currentcolor)").Image;
        var c = GradientSampling.Sample(g, .5);
        Assert.That((c.R,c.G,c.B,c.A), Is.EqualTo((0f,0f,1f,.5f)));
    }
    [Test]
    public void EndpointExtensionRetainsMissingComponentCarry()
    {
        var g = Gradient("background-image:linear-gradient(90deg in srgb,red,color(srgb none 0 0))").Image;
        Assert.That(GradientSampling.Sample(g, 2), Is.EqualTo(new Rgba(1,0,0,1)));
    }
    [Test]
    public void LongerHueBetweenEqualColorsStillTraversesAWholeTurn()
    {
        var g = Gradient("background-image:linear-gradient(90deg in hsl longer hue,red,red)").Image;
        var c = GradientSampling.Sample(g, .5);
        Assert.That(c.R, Is.EqualTo(0).Within(1e-6)); Assert.That(c.G, Is.EqualTo(1).Within(1e-6));
        Assert.That(c.B, Is.EqualTo(1).Within(1e-6));
    }
    [Test]
    public void ConicAngleStopsAndNegativeRepeatsRetainTheirPeriod()
    {
        var g = Gradient("background-image:repeating-conic-gradient(from -20deg at 25% 75%,red -90deg 0deg,blue 90deg)").Image;
        Assert.That(g.Kind, Is.EqualTo(GradientKind.Conic)); Assert.That(g.Repeating, Is.True);
        Assert.That(g.AxisX, Is.EqualTo(-20d/360).Within(1e-12));
        Assert.That((g.X,g.Y), Is.EqualTo((25d,60d)));
        Assert.That(g.Stops.Select(s => s.Position), Is.EqualTo(new[] { -.25, 0, .25 }));
        Assert.That(GradientSampling.Sample(g, -.1), Is.EqualTo(GradientSampling.Sample(g, .4)));
    }
    [Test]
    public void LayersCycleTheirPropertiesAndEmitBackToFrontWithBottomColorClip()
    {
        var (_, input, display) = Fixture("<div id=tile></div>", "#tile{width:100px;height:80px;padding:6px;border:4px solid black;"
            + "background-color:white;background-image:linear-gradient(red,blue),radial-gradient(yellow,green);"
            + "background-size:50% auto;background-position:right bottom;background-repeat:no-repeat;background-origin:content-box;"
            + "background-clip:border-box,content-box}");
        using var owner = input;
        var runs = display.Runs.OfType<GradientRun>().ToArray();
        Assert.That(runs.Select(r => r.Image.Kind), Is.EqualTo(new[] { GradientKind.Radial, GradientKind.Linear }));
        Assert.That(runs[0].Tile, Is.EqualTo(new Rect(60,10,50,80)));
        Assert.That(runs[0].Clip.Bounds, Is.EqualTo(new Rect(10,10,100,80)));
        Assert.That(runs[1].Clip.Bounds, Is.EqualTo(new Rect(0,0,120,100)));
        var color = display.Runs[display.Runs.IndexOf(runs[0]) - 1] as BoxRun;
        Assert.That(color, Is.Not.Null);
        Assert.That(color!.Style.Background, Is.EqualTo(new Rgba(1,1,1,1)));
        Assert.That(color.Bounds, Is.EqualTo(runs[0].Clip.Bounds));
    }
    [Test]
    public void RepeatRoundAndSpaceProduceExactTilePhaseAndStep()
    {
        var run = Gradient("background-image:linear-gradient(red,blue);background-size:30px 25px;background-repeat:round space;background-position:80% 40%");
        Assert.That(run.Tile.Width, Is.EqualTo(100d/3).Within(1e-12)); Assert.That(run.StepX, Is.EqualTo(run.Tile.Width));
        Assert.That(run.Tile.Y, Is.Zero); Assert.That(run.StepY, Is.EqualTo(27.5));
        Assert.That(run.RepeatX && run.RepeatY, Is.True);
    }
    [TestCase("background-size:0px 20px")]
    [TestCase("background-size:40px 20px;background-position:200px 0px")]
    public void InvisibleGeneratedTileDoesNotAllocateADisplayRunOrLoseItsSeparateBackgroundColor(string geometry)
    {
        var (_, input, display) = Fixture("<div id=tile></div>", "#tile{width:100px;height:80px;background-color:red;"
            + "background-image:linear-gradient(red,blue);background-repeat:no-repeat;" + geometry + "}");
        using var owner = input;
        Assert.That(display.Runs.OfType<GradientRun>(), Is.Empty);
        Assert.That(display.Runs.OfType<BoxRun>().Any(r => r.Style.Background == new Rgba(1,0,0,1)), Is.True);
    }
    [Test]
    public void DeferredBottomPlacementMovesGradientTileAndClipExactlyOnce()
    {
        var (_, input, display) = Fixture("<div id=parent><div id=tile></div></div>", "#parent{position:relative;width:100px;height:100px}"
            + "#tile{position:absolute;bottom:10px;width:80px;height:20px;background-image:linear-gradient(red,blue)}");
        using var owner = input;
        var run = display.Runs.OfType<GradientRun>().Single();
        Assert.That(run.Tile.Y, Is.EqualTo(70)); Assert.That(run.Clip.Bounds.Y, Is.EqualTo(70));
        var unpositioned = Gradient("width:80px;height:20px;background-image:linear-gradient(red,blue)").Image;
        Assert.That((run.Image.X,run.Image.Y,run.Image.AxisX,run.Image.AxisY,run.Image.Length),
            Is.EqualTo((unpositioned.X,unpositioned.Y,unpositioned.AxisX,unpositioned.AxisY,unpositioned.Length)));
        Assert.That(run.Image.Stops, Is.EqualTo(unpositioned.Stops));
    }
}
