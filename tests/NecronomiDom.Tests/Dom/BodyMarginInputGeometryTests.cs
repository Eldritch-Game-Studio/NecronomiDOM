using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class BodyMarginInputGeometryTests
{
    private sealed class TextFreeShaper : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => throw new InvalidOperationException("These geometry probes contain no painted text.");
    }
    private static (Document Doc, StyleEngine Styles, DisplayList Display) Layout(string markup)
    {
        var doc = HtmlImporter.Import("<!doctype html>" + markup, "body-margin", WebUrl.Parse("res:///body-margin.html")!).Document;
        var styles = new StyleEngine(doc);
        return (doc, styles, new FlowLayout(styles, new TextFreeShaper(), "serif").Layout(doc, 640, 480));
    }
    [Test]
    public void DefaultBodyMarginPreservesOriginalWptContentOriginsAndDimensions()
    {
        var (doc, _, display) = Layout("<style>div{padding:10px}</style><div id=parent><div id=child1></div><div id=child2></div></div><div id=done></div>");
        Rect Box(Element e) => display.Boxes.Single(b => b.Element == e).Bounds;
        Assert.That(Box(doc.DocumentElement!), Is.EqualTo(new Rect(0, 0, 640, 96)));
        Assert.That(Box(doc.Body!), Is.EqualTo(new Rect(8, 8, 624, 80)));
        Assert.That(Box(doc.GetElementById("parent")!), Is.EqualTo(new Rect(8, 8, 624, 60)));
        Assert.That(Box(doc.GetElementById("child1")!), Is.EqualTo(new Rect(18, 18, 604, 20)));
        Assert.That(Box(doc.GetElementById("child2")!), Is.EqualTo(new Rect(18, 38, 604, 20)));
        Assert.That(Box(doc.GetElementById("done")!), Is.EqualTo(new Rect(8, 68, 624, 20)));
        Assert.That(display.HitTest(320, 28), Is.SameAs(doc.GetElementById("child1")));
        Assert.That(display.HitTest(320, 48), Is.SameAs(doc.GetElementById("child2")));
    }
    [TestCase("content-box", 216, 56)]
    [TestCase("border-box", 200, 40)]
    public void RootBodyUsedBoxIncludesHorizontalAndVerticalMarginsPaddingAndBorder(string sizing, int width, int height)
    {
        var (doc, styles, display) = Layout($"<style>body{{margin:12px 20px;padding:6px;border:2px solid black;background:white;width:200px;height:40px;box-sizing:{sizing}}}</style><div></div>");
        var body = display.Boxes.Single(b => b.Element == doc.Body).Bounds;
        Assert.That(body, Is.EqualTo(new Rect(20, 12, width, height)));
        Assert.That(display.Boxes.Single(b => b.Element == doc.DocumentElement).Bounds.Height, Is.EqualTo(height + 24));
        Assert.That(display.Runs.OfType<BoxRun>().Any(r => r.Bounds == body && r.Style.BorderWidth == 2), Is.True, "Paint and input use the same used border box.");
        using var input = new DocumentInput(doc, styles); input.UpdateGeometry(display, 640, 480);
        Assert.That(input.HasPoint(500, 300), Is.True, "The actual propagated body background paints the root canvas beyond its content box.");
        EventTarget? target = null;
        doc.AddEventListener("pointermove", new DelegateEventListener(e => target = e.Target));
        input.PointerMove(500, 300); Assert.That(target, Is.SameAs(doc.DocumentElement));
        doc.DocumentElement!.SetAttribute("inert", ""); Assert.That(input.HasPoint(500, 300), Is.False);
    }
    [Test]
    public void NonRootVerticalMarginsRemainExplicitUnsupportedCases()
    {
        Assert.Throws<NotSupportedException>(() => Layout("<style>body{margin:0}div{margin-top:8px}</style><div></div>"));
        Assert.Throws<NotSupportedException>(() => Layout("<style>html{margin-top:8px}body{margin:0}</style>"));
    }
}
