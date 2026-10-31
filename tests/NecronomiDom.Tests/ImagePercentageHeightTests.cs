using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

public sealed class ImagePercentageHeightTests
{
    [TestCase("auto", "50%", null, false, 100, 50)]
    [TestCase("200px", "50%", null, false, 200, 100)]
    [TestCase("auto", "0%", null, false, 100, 50)]
    [TestCase("auto", "calc(50% + 10px)", null, false, 100, 50)]
    [TestCase("200px", "calc(50% + 10px)", null, false, 200, 100)]
    [TestCase("auto", "50%", 200, false, 200, 100)]
    [TestCase("auto", "0%", 200, false, 0, 0)]
    [TestCase("auto", "calc(50% + 10px)", 100, false, 120, 60)]
    [TestCase("auto", "30px", null, false, 60, 30)]
    [TestCase("auto", "calc(20px + 10px)", null, false, 60, 30)]
    [TestCase("auto", "50%", 100, true, 60, 30)]
    [TestCase("220px", "50%", null, true, 200, 100)]
    [TestCase("auto", "30px", null, true, 20, 10)]
    public void PercentageHeightUsesOnlyDefiniteContainingBlock(string width, string height, double? basis,
        bool borderBox, double expectedWidth, double expectedHeight)
    {
        var document = HtmlImporter.Import("<!doctype html><style>img{width:" + width + ";height:" + height +
            ";box-sizing:" + (borderBox ? "border-box" : "content-box") + "}</style><img>",
            "height", WebUrl.Parse("res:///ui/height.html")!).Document;
        var element = document.QuerySelector("img")!;
        var style = new StyleEngine(document).GetComputedStyle(element);
        var size = ImageLayout.Size(style, new ImageResource("image", 100, 50), 400, 20, 20, basis);
        Assert.That(size.Width, Is.EqualTo(expectedWidth).Within(1e-9));
        Assert.That(size.Height, Is.EqualTo(expectedHeight).Within(1e-9));
    }
}
