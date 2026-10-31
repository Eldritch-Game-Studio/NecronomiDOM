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
public sealed class FlowFlexTests
{
    private sealed class GlyphMetrics : ITextShaper
    { public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }
    private sealed class IntrinsicImage : IImageSource
    { public ImageResource Load(Element element) => new("memory:40x20", 40, 20); }
    private static (Document Doc, FlowLayout Flow) Fixture(string body, string css)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div,p,button,img{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;font-family:monospace;font-size:16px;line-height:20px;appearance:none}" +
            "html,body{width:100%;height:100%;background-color:white}" + css + "</style>" + body,
            "flex", WebUrl.Parse("res:///flex.html")!).Document;
        return (doc, new FlowLayout(new StyleEngine(doc), new GlyphMetrics(), "monospace", new IntrinsicImage()));
    }
    private static Rect Bounds(DisplayList display, string id) => display.Boxes.Single(b => b.Element.Id == id).Bounds;
    [Test]
    public void CenterColumnRetainsAuthoredBorderSizingMarginsAndResize()
    {
        var (doc, flow) = Fixture("<div id=parent><div id=stage></div></div>", "#parent{display:flex;flex-direction:column;align-items:center;justify-content:center;width:100%;height:200px}#stage{width:50%;height:80px;box-sizing:border-box;padding:10px;border:2px solid black;flex-shrink:0;margin-top:4px;margin-bottom:8px}");
        Assert.That(Bounds(flow.Layout(doc, 400, 300), "stage"), Is.EqualTo(new Rect(100, 58, 200, 80)));
        Assert.That(Bounds(flow.Layout(doc, 300, 300), "stage"), Is.EqualTo(new Rect(75, 58, 150, 80)));
    }
    [Test]
    public void RowUsesIntrinsicTextImageAndChildContentMutation()
    {
        var (doc, flow) = Fixture("<div id=parent><p id=label>AA</p><img id=image src=memory></div>", "#parent{display:flex;justify-content:space-between;align-items:center;width:200px;height:80px}");
        var first = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(first, "label"), Is.EqualTo(new Rect(0, 30, 16, 20)));
        Assert.That(Bounds(first, "image"), Is.EqualTo(new Rect(160, 30, 40, 20)));
        doc.GetElementById("label")!.TextContent = "AAAA";
        Assert.That(Bounds(flow.Layout(doc, 400, 300), "label").Width, Is.EqualTo(32));
    }
    [Test]
    public void GrowMaxFreezeAndStretchProduceActualUsedDescendantGeometry()
    {
        var (doc, flow) = Fixture("<div id=parent><div id=a></div><div id=b><div id=half></div></div></div>", "#parent{display:flex;width:200px;height:80px;align-items:stretch}#a{flex:1 1 40px;max-width:60px}#b{flex:1 1 40px}#half{height:50%}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "a"), Is.EqualTo(new Rect(0, 0, 60, 80)));
        Assert.That(Bounds(display, "b"), Is.EqualTo(new Rect(60, 0, 140, 80)));
        Assert.That(Bounds(display, "half").Height, Is.EqualTo(40));
    }
    [Test]
    public void AutomaticCrossHeightDoesNotCreateDefinitePercentageBasis()
    {
        var (doc, flow) = Fixture("<div id=parent><div id=item><div id=half><div id=content></div></div></div></div>", "#parent{display:flex;width:200px;align-items:flex-start}#item{width:100px}#half{height:50%}#content{height:24px}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "half").Height, Is.EqualTo(24));
        Assert.That(Bounds(display, "item").Height, Is.EqualTo(24));
    }
    [Test]
    public void RowReversePreservesDomHitOrderAndRelativeVisualOffsets()
    {
        var (doc, flow) = Fixture("<div id=parent><button id=a>A</button><button id=b>B</button></div>", "#parent{display:flex;flex-direction:row-reverse;width:200px;height:80px;align-items:center;justify-content:space-between}button{width:40px;height:20px;flex-shrink:0}#a{position:relative;left:5px;top:3px}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "a"), Is.EqualTo(new Rect(165, 33, 40, 20)));
        Assert.That(Bounds(display, "b"), Is.EqualTo(new Rect(0, 30, 40, 20)));
        Assert.That(display.HitTest(180, 40)?.Id, Is.EqualTo("a"));
    }
    [Test]
    public void AbsoluteChildDoesNotGrowLineAndUsesFlexStaticAlignment()
    {
        var (doc, flow) = Fixture("<div id=parent><div id=normal></div><div id=absolute></div></div>", "#parent{position:relative;display:flex;flex-direction:column;width:200px;height:100px;align-items:center;justify-content:center}#normal{width:40px;height:20px;flex-shrink:0}#absolute{position:absolute;width:60px;height:30px;bottom:10px;align-self:center}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "normal"), Is.EqualTo(new Rect(80, 40, 40, 20)));
        Assert.That(Bounds(display, "absolute"), Is.EqualTo(new Rect(70, 60, 60, 30)));
    }
    [Test]
    public void PercentCardsRetainShrinkZeroAndCenteredOverflow()
    {
        var (doc, flow) = Fixture("<div id=parent><div id=a></div><div id=b></div></div>", "#parent{display:flex;width:200px;justify-content:center;align-items:flex-start}#a,#b{width:60%;height:20px;flex-shrink:0;margin-left:8px;margin-right:8px}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "a"), Is.EqualTo(new Rect(-28, 0, 120, 20)));
        Assert.That(Bounds(display, "b"), Is.EqualTo(new Rect(108, 0, 120, 20)));
    }
    [Test]
    public void AnonymousTextFlexItemWrapsAtItsResolvedWidth()
    {
        var (doc, flow) = Fixture("<div id=parent>AA AA</div>", "#parent{display:flex;width:24px;align-items:center}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "parent").Height, Is.EqualTo(40));
        Assert.That(display.Runs.OfType<TextRun>().Select(r => r.Text), Is.EqualTo(new[] { "AA", "AA" }));
    }
    [Test]
    public void AnonymousColumnStretchUsesContainingWidthForTextAlignmentAndWrapping()
    {
        var (doc, flow) = Fixture("<div id=parent>AA AA</div>",
            "#parent{display:flex;flex-direction:column;width:24px;align-items:stretch;text-align:center}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "parent").Height, Is.EqualTo(40));
        Assert.That(display.Runs.OfType<TextRun>().Select(r => r.X), Is.EqualTo(new[] { 4d, 4d }));
    }
    [Test]
    public void MixedAnonymousItemsContributeRealIntrinsicWidthIncludingGaps()
    {
        var (doc, flow) = Fixture("<div id=parent><div id=inner>AA<div id=box></div>AA</div></div>",
            "#parent{display:flex;flex-direction:column;width:200px;height:80px;align-items:center;justify-content:center}#inner{display:flex;column-gap:10px;align-items:center}#box{width:20px;height:10px}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "inner"), Is.EqualTo(new Rect(64, 30, 72, 20)));
        Assert.That(Bounds(display, "box"), Is.EqualTo(new Rect(90, 35, 20, 10)));
    }
    [Test]
    public void ColumnAutomaticMinimumUsesContentNotAuthoredHeight()
    {
        var (doc, flow) = Fixture("<div id=parent><p id=item>AA</p></div>", "#parent{display:flex;flex-direction:column;width:100px;height:60px}#item{height:100px;flex-shrink:1}");
        Assert.That(Bounds(flow.Layout(doc, 400, 300), "item").Height, Is.EqualTo(60));
    }

    [Test]
    public void ReplacedContentBasisIgnoresAuthoredMainSizeAndRetainsIntrinsicRatio()
    {
        var (doc, flow) = Fixture("<div id=parent><img id=image src=memory></div>",
            "#parent{display:flex;width:200px;height:80px;align-items:center}#image{width:100px;flex-basis:content}");
        Assert.That(Bounds(flow.Layout(doc, 400, 300), "image"), Is.EqualTo(new Rect(0, 30, 40, 20)));
    }
    [Test]
    public void IndefinitePercentageBasisUsesContentRatherThanAuthoredMainHeight()
    {
        var (doc, flow) = Fixture("<div id=parent><p id=item>AA</p></div>",
            "#parent{display:flex;flex-direction:column;width:100px}#item{height:100px;flex-basis:50%}");
        var display = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(display, "parent").Height, Is.EqualTo(20));
        Assert.That(Bounds(display, "item").Height, Is.EqualTo(20));
    }

    [Test]
    public void UnsupportedColumnWrappingIsExplicit()
    {
        var (doc, flow) = Fixture("<div id=parent><div></div></div>", "#parent{display:flex;flex-direction:column;flex-wrap:wrap}");
        Assert.Throws<NotSupportedException>(() => flow.Layout(doc, 400, 300));
    }
}
