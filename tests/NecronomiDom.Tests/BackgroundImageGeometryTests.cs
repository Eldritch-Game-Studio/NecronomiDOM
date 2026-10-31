using NecronomiDom.Css.Values;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests;

public sealed class BackgroundImageGeometryTests
{
    [Test]
    public void NegativeFreeSpaceCentersTheIntrinsicCrop()
    {
        var run = ImageLayout.Place(new("glyph",128,64), new(0,0,72,72), new(0,0,72,72),
            new(144,72), new CssPercentageValue(50), new CssPercentageValue(50), ImageSampling.Linear)!;
        Assert.That(run.Destination, Is.EqualTo(new Rect(0,0,72,72)));
        Assert.That(run.Source, Is.EqualTo(new Rect(32,0,64,64)));
    }
    [Test]
    public void PositioningAndPaintingAreasAreIndependent()
    {
        var run = ImageLayout.Place(new("glyph",128,64), new(20,30,80,80), new(10,20,100,100),
            new(160,80), new CssPercentageValue(50), new CssPercentageValue(50), ImageSampling.Nearest)!;
        Assert.That(run.Destination, Is.EqualTo(new Rect(10,30,100,80)));
        Assert.That(run.Source, Is.EqualTo(new Rect(24,0,80,64)));
        Assert.That(run.Sampling, Is.EqualTo(ImageSampling.Nearest));
    }
    [TestCase(80,0)]
    [TestCase(79.5,.5)]
    public void TouchingAndSubpixelClipEdgesRemainDistinct(double position, double visibleWidth)
    {
        var run = ImageLayout.Place(new("glyph",10,10), new(0,0,80,80), new(0,0,80,80),
            new(10,10), new CssDimensionValue(position,"px"), new CssDimensionValue(0,"px"), ImageSampling.Linear);
        if (visibleWidth == 0) Assert.That(run, Is.Null);
        else
        {
            Assert.That(run!.Destination.Width, Is.EqualTo(visibleWidth));
            Assert.That(run.Source.Width, Is.EqualTo(visibleWidth));
        }
    }
    [Test]
    public void EmptyBackgroundPaintingAreaHasNoTile()
    {
        Assert.That(ImageLayout.Place(new("glyph",10,10), new(0,0,80,80), new(0,0,0,80),
            new(10,10), new CssPercentageValue(50), new CssPercentageValue(50), ImageSampling.Linear), Is.Null);
    }
}
