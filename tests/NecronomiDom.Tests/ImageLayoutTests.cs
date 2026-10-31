using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

public sealed class ImageLayoutTests
{
    private static (Document Document, HTMLImageElement Image, StyleEngine Styles) Fixture(string css = "")
    {
        var d = HtmlImporter.Import("<!doctype html><style>img { " + css + " }</style><img src='relative.png'>", "image", WebUrl.Parse("res:///ui/image.html")!).Document;
        return (d, (HTMLImageElement)d.QuerySelector("img")!, new StyleEngine(d));
    }
    [TestCase("fill", 0, 0, 200, 100, 80, 40)]
    [TestCase("contain", 0, 0, 200, 100, 80, 40)]
    [TestCase("none", 60, 30, 80, 40, 80, 40)]
    [TestCase("scale-down", 60, 30, 80, 40, 80, 40)]
    public void ConcreteSizing(string fit, double x, double y, double width, double height, double sourceWidth, double sourceHeight)
    {
        var f = Fixture("object-fit:" + fit);
        var run = ImageLayout.Paint(f.Styles.GetComputedStyle(f.Image), new("relative", 80, 40), new(0, 0, 200, 100))!;
        Assert.That(run.Destination, Is.EqualTo(new Rect(x, y, width, height)));
        Assert.That(run.Source, Is.EqualTo(new Rect(0, 0, sourceWidth, sourceHeight)));
    }
    [TestCase("25% 75%", 20, 0, 80, 40)]
    [TestCase("right 10px bottom 5px", 84, 2, 76, 38)]
    [TestCase("calc(25% + 5px) calc(50% - 5px)", 18, 2, 80, 38)]
    public void CoverUsesSignedFreeSpaceAndTypedEdgeCalculations(string position, double x, double y, double width, double height)
    {
        var f = Fixture("object-fit:cover;object-position:" + position);
        var run = ImageLayout.Paint(f.Styles.GetComputedStyle(f.Image), new("relative", 160, 40), new(0, 0, 200, 100))!;
        Assert.That(run.Source.X, Is.EqualTo(x).Within(1e-9)); Assert.That(run.Source.Y, Is.EqualTo(y).Within(1e-9));
        Assert.That(run.Source.Width, Is.EqualTo(width).Within(1e-9)); Assert.That(run.Source.Height, Is.EqualTo(height).Within(1e-9));
    }
    [Test]
    public void TouchingClipEdgeHasNoPaintAndSubpixelIntersectionDoes()
    {
        var f = Fixture("object-fit:none;object-position:100px 0px");
        Assert.That(ImageLayout.Paint(f.Styles.GetComputedStyle(f.Image), new("r", 10, 10), new(0, 0, 100, 100)), Is.Null);
        f.Image.Style.SetProperty("object-position", "99.5px 0px");
        var run = ImageLayout.Paint(f.Styles.GetComputedStyle(f.Image), new("r", 10, 10), new(0, 0, 100, 100))!;
        Assert.That(run.Destination.Width, Is.EqualTo(.5)); Assert.That(run.Source.Width, Is.EqualTo(.5));
    }
    [Test]
    public void IntrinsicRatioAndBorderBoxDeclaredSize()
    {
        var f = Fixture("width:100px;height:auto;box-sizing:border-box");
        Assert.That(ImageLayout.Size(f.Styles.GetComputedStyle(f.Image), new("r", 160, 40), 500, 20, 30), Is.EqualTo(new ImageSize(80, 20)));
        f.Image.Style.SetProperty("width", "auto"); f.Image.Style.SetProperty("height", "70px");
        Assert.That(ImageLayout.Size(f.Styles.GetComputedStyle(f.Image), new("r", 160, 40), 500, 20, 30), Is.EqualTo(new ImageSize(160, 40)));
    }
    [Test]
    public void ImageDimensionHintsLoseToAuthorLayersAndExplicitAuto()
    {
        var f = Fixture(); f.Image.SetAttribute("width", "120"); f.Image.SetAttribute("height", "30");
        Assert.That(f.Styles.GetComputedStyle(f.Image).GetPropertyValue("width"), Is.EqualTo("120px"));
        f.Document.StyleSheets.Add(NecronomiDom.Css.Om.CSSStyleSheet.Parse("@layer a { img { width:auto; } }"));
        Assert.That(f.Styles.GetComputedStyle(f.Image).GetPropertyValue("width"), Is.EqualTo("auto"));
    }
    [Test]
    public void SuccessfulDecodeStateInvalidatesWhenSourceMutates()
    {
        var f = Fixture(); Assert.That(f.Image.NaturalWidth, Is.Zero); Assert.That(f.Image.Complete, Is.False);
        f.Image.SetDecodedSource("res:///ui/relative.png", 160, 40);
        Assert.That(f.Image.NaturalWidth, Is.EqualTo(160)); Assert.That(f.Image.CurrentSrc, Is.EqualTo("res:///ui/relative.png"));
        f.Image.Src = "new.png"; Assert.That(f.Image.NaturalWidth, Is.Zero); Assert.That(f.Image.Complete, Is.False);
        Assert.That(f.Image.Src, Is.EqualTo("res:///ui/new.png"));
    }
    [TestCase("data:image/png;base64,YWI", new byte[] { 97, 98 })]
    [TestCase("data:image/png; BASE64,YW%20I=", new byte[] { 97, 98 })]
    [TestCase("data:image/png,%FF%00a", new byte[] { 255, 0, 97 })]
    public void AllowedDataBytesUsePolicyPercentAndForgivingBase64(string source, byte[] bytes)
    {
        var resolution = ResourceUrlPolicy.Default.Resolve(source, WebUrl.Parse("res:///ui/image.html")!, ResourceKind.Image);
        Assert.That(resolution.Allowed, Is.True);
        Assert.That(ResourceDataBody.Decode(resolution), Is.EqualTo(bytes));
    }
    [Test]
    public void RejectedDataCannotBeDecoded()
    {
        var resolution = ResourceUrlPolicy.Default.Resolve("data:image/png;base64,a", WebUrl.Parse("res:///ui/image.html")!, ResourceKind.Image);
        Assert.That(resolution.Allowed, Is.False);
        Assert.Throws<ArgumentException>(() => ResourceDataBody.Decode(resolution));
    }
    [Test]
    public void MissingSourceReflectionIsEmptyNotTheDocumentUrl()
    {
        var f = Fixture(); f.Image.RemoveAttribute("src");
        Assert.That(f.Image.Src, Is.Empty); Assert.That(f.Image.Complete, Is.True); Assert.That(f.Image.NaturalWidth, Is.Zero);
    }
    [Test]
    public void ScaleDownShrinksButNeverUpscales()
    {
        var f = Fixture("object-fit:scale-down");
        var style = f.Styles.GetComputedStyle(f.Image);
        Assert.That(ImageLayout.Paint(style, new("r", 160, 40), new(0, 0, 80, 80))!.Destination, Is.EqualTo(new Rect(0, 30, 80, 20)));
        Assert.That(ImageLayout.Paint(style, new("r", 16, 4), new(0, 0, 80, 80))!.Destination, Is.EqualTo(new Rect(32, 38, 16, 4)));
    }
    [Test]
    public void FontRelativeCalcUsesComputedLengthsForSizingAndPosition()
    {
        var f = Fixture("font-size:10px; object-fit:none; width:calc(50% - 1em); height:auto; object-position:calc(25% + 1em) calc(50% - 1em)");
        var style = f.Styles.GetComputedStyle(f.Image);
        var image = new ImageResource("r", 40, 20);
        Assert.That(ImageLayout.Size(style, image, 100, 0, 0), Is.EqualTo(new ImageSize(40, 20)));
        Assert.That(ImageLayout.Paint(style, image, new(0, 0, 80, 80))!.Destination, Is.EqualTo(new Rect(20, 20, 40, 20)));
    }
    [Test]
    public void HalfPixelImageRasterBoundsRoundBothEdgesWithoutChangingLayout()
    {
        var layout = new Rect(90, 139.5, 30, 15);
        Assert.That(ImageLayout.RasterBounds(layout), Is.EqualTo(new Rect(90, 140, 30, 15)));
        Assert.That(layout.Y, Is.EqualTo(139.5));
        Assert.That(ImageLayout.RasterBounds(new(0.25, 0.25, 0.3, 0.3)), Is.EqualTo(new Rect(0, 0, 1, 1)));
    }
    [TestCase("auto", ImageSampling.Linear)]
    [TestCase("smooth", ImageSampling.Linear)]
    [TestCase("high-quality", ImageSampling.Linear)]
    [TestCase("crisp-edges", ImageSampling.Nearest)]
    [TestCase("pixelated", ImageSampling.Pixelated)]
    public void SupportedRenderingValuesReachTheNativeSampler(string rendering, ImageSampling expected)
    {
        var f = Fixture("image-rendering:" + rendering);
        Assert.That(ImageLayout.Paint(f.Styles.GetComputedStyle(f.Image), new("r", 16, 4), new(0, 0, 80, 80))!.Sampling, Is.EqualTo(expected));
    }
}
