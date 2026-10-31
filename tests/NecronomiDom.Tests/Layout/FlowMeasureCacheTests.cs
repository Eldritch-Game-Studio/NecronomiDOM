using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class FlowMeasureCacheTests
{
    private sealed class Metrics : ITextShaper
    {
        public int Calls;
        public double Advance = 8;
        public bool Throw;
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("measurement failure");
            return new(text.Length * Advance, 12, 4);
        }
    }

    private sealed class Images : IImageSource
    {
        public double Width = 40;
        public ImageResource Load(Element element) => new("memory:mutable", Width, 20);
    }

    private static (Document Doc, FlowLayout Flow, Metrics Metrics, Images Images) Fixture(string body, string css = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div,p,img{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;font-family:monospace;font-size:16px;line-height:20px;appearance:none}" +
            "html,body{width:100%;height:100%}div{display:flex;align-items:flex-start}" + css + "</style>" + body,
            "measure-cache", WebUrl.Parse("res:///measure-cache.html")!).Document;
        var metrics = new Metrics();
        var images = new Images();
        return (doc, new FlowLayout(new StyleEngine(doc), metrics, "monospace", images), metrics, images);
    }

    private static Rect Bounds(DisplayList display, string id) => display.Boxes.Single(b => b.Element.Id == id).Bounds;

    [Test]
    public void DeepNestedFlexDoesNotRepeatTheSameMeasurementExponentially()
    {
        const int depth = 10;
        string body = string.Concat(Enumerable.Repeat("<div>", depth)) + "<p id=leaf>AA AA</p>" +
            string.Concat(Enumerable.Repeat("</div>", depth));
        var (doc, flow, metrics, _) = Fixture(body);
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "leaf"), Is.EqualTo(new Rect(0, 0, 40, 20)));
        Assert.That(metrics.Calls, Is.LessThan(200), "Only distinct measurement plans may walk the leaf; not every ancestor branch");
        Assert.That(display.Boxes.Count(b => b.Element.LocalName == "div"), Is.EqualTo(depth));
        Assert.That(display.Runs.OfType<TextRun>().Single().Text, Is.EqualTo("AA AA"));
        Assert.That(display.HitTest(4, 4)?.Id, Is.EqualTo("leaf"));
    }

    [Test]
    public void WidthDependentWrappingIsNotReusedFromTheIntrinsicOrEarlierPlan()
    {
        var (doc, flow, _, _) = Fixture("<div id=parent><div id=item><p id=leaf>AA AA</p></div></div>",
            "#parent{width:24px}#item{min-width:0;flex:1 1 auto}");
        Assert.That(Bounds(flow.Layout(doc, 400, 300), "item").Height, Is.EqualTo(40));
        ((HTMLElement)doc.GetElementById("parent")!).Style.SetProperty("width", "80px");
        Assert.That(Bounds(flow.Layout(doc, 400, 300), "item").Height, Is.EqualTo(20));
    }

    [Test]
    public void NextPassRereadsTextFontMetricsStylesAndImageDimensions()
    {
        var (doc, flow, metrics, images) = Fixture("<div><p id=leaf>AA</p><img id=image src=memory></div>");
        var first = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(first, "leaf").Width, Is.EqualTo(16));
        Assert.That(Bounds(first, "image").Width, Is.EqualTo(40));
        doc.GetElementById("leaf")!.TextContent = "AAAA";
        metrics.Advance = 10;
        images.Width = 60;
        var second = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(second, "leaf").Width, Is.EqualTo(40));
        Assert.That(Bounds(second, "image").Width, Is.EqualTo(60));
        ((HTMLElement)doc.GetElementById("leaf")!).Style.SetProperty("display", "none");
        Assert.That(flow.Layout(doc, 400, 300).Boxes.Any(b => b.Element.Id == "leaf"), Is.False);
    }

    [Test]
    public void FailedPassCannotLeaveMeasurementsForTheNextDocument()
    {
        var (doc, flow, metrics, _) = Fixture("<div><p id=first>AA</p><p id=second>BB</p></div>");
        metrics.Throw = true;
        Assert.Throws<InvalidOperationException>(() => flow.Layout(doc, 400, 300));
        metrics.Throw = false;
        doc.GetElementById("first")!.TextContent = "AAAA";
        Assert.That(Bounds(flow.Layout(doc, 400, 300), "first").Width, Is.EqualTo(32));
        doc.GetElementById("second")!.Remove();
        Assert.That(flow.Layout(doc, 400, 300).Boxes.Any(b => b.Element.Id == "second"), Is.False);
    }
}
