using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class NoEffectOriginValidationTests
{
    private sealed class TextFreeShaper : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => throw new InvalidOperationException("The origin probes contain no painted text.");
    }
    private static DisplayList Layout(string declaration)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>body{margin:0}div{width:80px;height:40px;" + declaration + "}</style><div id=origin></div>", "origin", WebUrl.Parse("res:///origin.html")!).Document;
        var styles = new StyleEngine(doc);
        return new FlowLayout(styles, new TextFreeShaper(), "serif").Layout(doc, 640, 480);
    }
    [TestCase("")]
    [TestCase("transform-origin:left top;perspective-origin:right bottom")]
    public void ComputedOriginsDoNotEnableAnAbsentEffect(string declaration)
    {
        var display = Layout(declaration);
        Assert.That(display.Boxes.Single(box => box.Element.Id == "origin").Bounds, Is.EqualTo(new Rect(0, 0, 80, 40)));
    }
    [TestCase("transform:rotateX(1deg)")]
    [TestCase("perspective:100px")]
    public void ThreeDimensionalTransformAndPerspectiveRemainExplicitlyUnsupported(string declaration)
    {
        Assert.Throws<NotSupportedException>(() => Layout(declaration));
    }
    [Test]
    public void ActualTwoDimensionalTranslationUsesItsFinalOriginAndPublicHitGeometry()
    {
        var display = Layout("transform:translateX(1px)");
        var box = display.Boxes.Single(candidate => candidate.Element.Id == "origin");
        Assert.That(box.Bounds, Is.EqualTo(new Rect(0, 0, 80, 40)));
        Assert.That(box.VisualBounds, Is.EqualTo(new Rect(1, 0, 80, 40)));
        Assert.That(display.HitTest(80.5, 20)?.Id, Is.EqualTo("origin"));
        Assert.That(display.HitTest(0, 20)?.Id, Is.Not.EqualTo("origin"));
    }
}
