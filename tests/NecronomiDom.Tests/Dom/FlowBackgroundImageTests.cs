using System;
using System.Linq;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    private sealed class BackgroundResources : IImageSource, IBackgroundImageSource
    {
        public string Url = "";
        public string OwnerTag = "";
        public int Loads;
        public ImageResource Load(Element element) => throw new InvalidOperationException("A CSS background must not manufacture an img element.");
        public ImageResource LoadBackground(Element owner, string sourceUrl)
        { Loads++; Url = sourceUrl; OwnerTag = owner.LocalName; return new("memory:40x20-background", 40, 20); }
    }
    private const string BackgroundCss = "#tile{width:100px;height:80px;padding:6px;border:4px solid black;border-radius:20px;"
        + "background-image:url(images/icon.png);background-size:contain;background-position:center;background-repeat:no-repeat}";
    [Test]
    public void RealBackgroundUsesResolvedUrlAndColorImageBorderContentPaintOrder()
    {
        var resources = new BackgroundResources();
        var (doc, input, display) = Fixture("<div id=tile><div id=child></div></div>", BackgroundCss
            + "#tile{background-color:red}#child{width:20px;height:20px;background-color:blue}", resources);
        using var owner = input;
        var background = display.Runs.OfType<BackgroundImageRun>().Single();
        Assert.That(background.Image.Destination, Is.EqualTo(new Rect(4, 22, 112, 56)));
        Assert.That(background.Clip.Bounds, Is.EqualTo(new Rect(0, 0, 120, 100)));
        Assert.That(resources.Url, Is.EqualTo("res:///images/icon.png")); Assert.That(resources.OwnerTag, Is.EqualTo("div"));
        Assert.That(doc.QuerySelectorAll("img").Length, Is.Zero);
        int ordinal = display.Runs.IndexOf(background);
        Assert.That(display.Runs[ordinal - 1], Is.InstanceOf<BoxRun>());
        Assert.That(((BoxRun)display.Runs[ordinal - 1]).Style.Background, Is.EqualTo(new Rgba(1, 0, 0, 1)));
        var border = (BoxRun)display.Runs[ordinal + 1];
        Assert.That(border.Style.Background.A, Is.Zero); Assert.That(border.Style.BorderWidth, Is.EqualTo(4));
        Assert.That(Click(doc, input, 15, 15), Is.EqualTo("child"));
    }
    [Test]
    public void ContentPositioningAndPaddingClipHaveIndependentActualInsets()
    {
        var (_, input, display) = Fixture("<div id=tile></div>", BackgroundCss
            + "#tile{background-origin:content-box;background-clip:padding-box}", new BackgroundResources());
        using var owner = input;
        var background = display.Runs.OfType<BackgroundImageRun>().Single();
        Assert.That(background.Image.Destination, Is.EqualTo(new Rect(10, 25, 100, 50)));
        Assert.That(background.Clip.Bounds, Is.EqualTo(new Rect(4, 4, 112, 92)));
        Assert.That(background.Clip.Radii.TopLeft, Is.EqualTo(new BorderCorner(16, 16)));
    }
    [Test]
    public void CoverUsesSignedFreeSpaceAndCropsIntrinsicPixelsToPaintingEdge()
    {
        var (_, input, display) = Fixture("<div id=tile></div>", BackgroundCss + "#tile{background-size:cover}", new BackgroundResources());
        using var owner = input;
        var run = display.Runs.OfType<BackgroundImageRun>().Single().Image;
        Assert.That(run.Destination, Is.EqualTo(new Rect(0, 4, 120, 92)));
        Assert.That(run.Source.X, Is.EqualTo(32 * 40 / 184d).Within(1e-12));
        Assert.That(run.Source.Width, Is.EqualTo(120 * 40 / 184d).Within(1e-12));
        Assert.That(run.Source.Height, Is.EqualTo(20));
    }
    [Test]
    public void PercentageAndAutoSizePreserveIntrinsicRatioAndActualPositioningBasis()
    {
        var (_, input, display) = Fixture("<div id=tile></div>", BackgroundCss
            + "#tile{background-size:50% auto;background-position:100% 0%}", new BackgroundResources());
        using var owner = input;
        Assert.That(display.Runs.OfType<BackgroundImageRun>().Single().Image.Destination, Is.EqualTo(new Rect(60, 4, 56, 28)));
    }
    [Test]
    public void DeferredBottomTranslationMovesTileAndItsOwnedEllipticalClipTogether()
    {
        var (_, input, display) = Fixture("<div id=parent><div id=tile></div></div>", "#parent{position:relative;width:100px;height:100px}"
            + "#tile{position:absolute;bottom:10px;width:80px;height:20px;background-image:url(images/icon.png);"
            + "background-size:contain;background-position:center;background-repeat:no-repeat;border-radius:12px}", new BackgroundResources());
        using var owner = input;
        var run = display.Runs.OfType<BackgroundImageRun>().Single();
        Assert.That(run.Image.Destination, Is.EqualTo(new Rect(20, 70, 40, 20))); Assert.That(run.Clip.Bounds.Y, Is.EqualTo(70));
    }
    [Test]
    public void NoImageRetainsNormalFastPathAndDoesNotLoadInactiveSizeOrPosition()
    {
        var resources = new BackgroundResources();
        var (_, input, display) = Fixture("<div id=tile></div>", "#tile{width:100px;height:80px;background-color:red;"
            + "background-image:none;background-size:cover;background-position:right bottom}", resources);
        using var owner = input;
        Assert.That(resources.Loads, Is.Zero); Assert.That(display.Runs.OfType<BackgroundImageRun>(), Is.Empty);
        Assert.That(display.Runs.OfType<BoxRun>().Any(run => run.Style.Background == new Rgba(1, 0, 0, 1)), Is.True);
    }
    [Test]
    public void ColorOnlyContentClipUsesActualBorderAndPaddingInsets()
    {
        var (_, input, display) = Fixture("<div id=tile></div>", BackgroundCss
            + "#tile{background-image:none;background-color:red;background-clip:content-box}", new BackgroundResources());
        using var owner = input;
        var color = display.Runs.OfType<BoxRun>().Single(run => run.Style.Background == new Rgba(1, 0, 0, 1));
        Assert.That(color.Bounds, Is.EqualTo(new Rect(10, 10, 100, 80))); Assert.That(color.Style.Radius, Is.EqualTo(10));
    }
    [Test]
    public void EmptyPaintingEdgeDoesNotDecodeOrEmitBackgroundTile()
    {
        var resources = new BackgroundResources();
        var (_, input, display) = Fixture("<div id=tile></div>", BackgroundCss
            + "#tile{height:0px;border-width:0px;padding:0px}", resources);
        using var owner = input;
        Assert.That(resources.Loads, Is.Zero); Assert.That(display.Runs.OfType<BackgroundImageRun>(), Is.Empty);
    }
    [TestCase("background-repeat:repeat")]
    public void ActiveUnsupportedBackgroundModesRemainExplicit(string unsupported)
    {
        Assert.Throws<NotSupportedException>(() => Fixture("<div id=tile></div>", BackgroundCss + "#tile{" + unsupported + "}", new BackgroundResources()));
    }
    [Test]
    public void MultipleUrlLayersUseOneNativeSourceAndPaintAuthoredFirstLayerLast()
    {
        var resources = new BackgroundResources();
        var (_, input, display) = Fixture("<div id=tile></div>", BackgroundCss
            + "#tile{background-image:url(images/icon.png),url(images/second.png);"
            + "background-size:10px 8px,20px 12px;background-position:left top,right bottom}", resources);
        using var owner = input;
        var backgrounds = display.Runs.OfType<BackgroundImageRun>().ToArray();
        Assert.That(resources.Loads, Is.EqualTo(2));
        Assert.That(backgrounds, Has.Length.EqualTo(2));
        Assert.That(backgrounds[0].Image.Destination.Width, Is.EqualTo(20));
        Assert.That(backgrounds[0].Image.Destination.Height, Is.EqualTo(12));
        Assert.That(backgrounds[1].Image.Destination.Width, Is.EqualTo(10));
        Assert.That(backgrounds[1].Image.Destination.Height, Is.EqualTo(8));
        Assert.That(resources.Url, Does.EndWith("images/icon.png"));
    }
    [Test]
    public void TypedGradientUsesProductionBackgroundPathWithoutLoadingUrlResources()
    {
        var resources = new BackgroundResources();
        var (_, input, display) = Fixture("<div id=tile></div>", BackgroundCss
            + "#tile{background-image:linear-gradient(red,blue);background-size:20px 10px;background-position:left top}", resources);
        using var owner = input;
        var gradient = display.Runs.OfType<GradientRun>().Single();
        Assert.That(resources.Loads, Is.Zero);
        Assert.That(gradient.Image.Kind, Is.EqualTo(GradientKind.Linear));
        Assert.That(gradient.Tile.Width, Is.EqualTo(20));
        Assert.That(gradient.Tile.Height, Is.EqualTo(10));
        Assert.That(display.Runs.OfType<BackgroundImageRun>(), Is.Empty);
    }
    [Test]
    public void DeferredPositionedGradientTranslatesTileAndClipButKeepsTileLocalImage()
    {
        var resources = new BackgroundResources();
        var (_, unpositionedInput, unpositionedDisplay) = Fixture("<div id=tile></div>", BackgroundCss
            + "#tile{background-image:linear-gradient(red,blue);background-size:20px 10px;background-position:left top}", resources);
        using var unpositionedOwner = unpositionedInput;
        var unpositionedImage = unpositionedDisplay.Runs.OfType<GradientRun>().Single().Image;
        var (_, input, display) = Fixture("<div id=outer><div id=tile></div></div>", BackgroundCss
            + "#outer{position:relative;width:200px;height:160px}"
            + "#tile{position:absolute;left:11px;bottom:7px;background-image:linear-gradient(red,blue);"
            + "background-size:20px 10px;background-position:left top}", resources);
        using var owner = input;
        var gradient = display.Runs.OfType<GradientRun>().Single();
        Assert.That(gradient.Clip.Bounds, Is.EqualTo(new Rect(11, 53, 120, 100)));
        Assert.That(gradient.Tile, Is.EqualTo(new Rect(15, 57, 20, 10)));
        Assert.That(gradient.Image.Kind, Is.EqualTo(GradientKind.Linear));
        Assert.That(gradient.Image.Length, Is.EqualTo(unpositionedImage.Length));
        Assert.That(gradient.Image.X, Is.EqualTo(unpositionedImage.X));
        Assert.That(gradient.Image.Y, Is.EqualTo(unpositionedImage.Y));
        Assert.That(gradient.Image.AxisX, Is.EqualTo(unpositionedImage.AxisX));
        Assert.That(gradient.Image.AxisY, Is.EqualTo(unpositionedImage.AxisY));
        Assert.That(gradient.Image.Stops, Is.EqualTo(unpositionedImage.Stops));
        Assert.That(resources.Loads, Is.Zero);
    }
}
