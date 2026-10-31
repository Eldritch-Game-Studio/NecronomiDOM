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
public sealed partial class ScrollContainerTests
{
    private sealed class TextMetrics : ITextShaper
    {
        public NecronomiDom.Layout.TextMetrics Measure(string text, int size, double weight = 400, double spacing = 0, bool italic = false)
            => new(text.Length * 8, 12, 4);
    }
    private sealed class View : IDisposable
    {
        internal readonly Document Doc;
        internal readonly StyleEngine Styles;
        internal readonly FlowLayout Layout;
        internal readonly DocumentInput Input;
        internal DisplayList Display = new();
        internal View(string markup, string css = "")
        {
            Doc = HtmlImporter.Import("<!doctype html><style>*{box-sizing:content-box;font-family:monospace;font-size:16px;line-height:16px;scrollbar-width:none}html,body{margin:0;padding:0}button{display:block;appearance:none;padding:0;border:0} " + css + "</style>" + markup,
                "scroll", WebUrl.Parse("res:///scroll.html")!).Document;
            Styles = new StyleEngine(Doc); Layout = new FlowLayout(Styles, new TextMetrics(), "monospace");
            Input = new DocumentInput(Doc, Styles); Update();
        }
        internal Element E(string id) => Doc.GetElementById(id)!;
        internal Rect Bounds(string id) => Display.Boxes.Single(b => b.Element.Id == id).Bounds;
        internal void Update() { Display = Layout.Layout(Doc, 400, 300); Input.UpdateGeometry(Display, 400, 300); }
        public void Dispose() => Input.Dispose();
    }
    [TestCase("auto")]
    [TestCase("scroll")]
    public void FractionalOffsetsClampAndTranslatePaintAndHitGeometry(string overflow)
    {
        using var v = new View("<div id=scroller><button id=child></button></div>",
            $"#scroller{{width:100px;height:80px;padding:5px;border:2px solid black;overflow:{overflow}}}#child{{width:240px;height:210px;background:red}}");
        var scroller = v.E("scroller");
        Assert.That((scroller.ClientWidth, scroller.ClientHeight, scroller.ScrollWidth, scroller.ScrollHeight), Is.EqualTo((110, 90, 250, 220)));
        scroller.ScrollTo(23.5, 31.25); v.Update();
        Assert.That(v.Bounds("scroller"), Is.EqualTo(new Rect(0, 0, 114, 94)));
        Assert.That(v.Bounds("child"), Is.EqualTo(new Rect(-16.5, -24.25, 240, 210)));
        Assert.That(v.Display.HitTest(10, 10)?.Id, Is.EqualTo("child"));
        Assert.That(v.Display.HitTest(120, 20)?.Id, Is.Not.EqualTo("child"));
        Assert.That(v.Display.Runs.OfType<BoxRun>().Single(r => r.Style.Background.R == 1 && r.Style.Background.G == 0).Bounds, Is.EqualTo(v.Bounds("child")));
        scroller.ScrollTo(10000, 10000); Assert.That((scroller.ScrollLeft, scroller.ScrollTop), Is.EqualTo((140d, 130d)));
        scroller.ScrollTo(-1, double.NaN); Assert.That((scroller.ScrollLeft, scroller.ScrollTop), Is.EqualTo((0d, 0d)));
    }
    [Test]
    public void NestedClippedOverflowDoesNotInflateOuterAreaAndOffsetsCompose()
    {
        using var v = new View("<div id=outer><div id=before></div><div id=inner><button id=row></button></div><div id=after></div></div>",
            "#outer{width:160px;height:100px;overflow:auto}#before{height:40px}#inner{width:120px;height:60px;overflow:auto}#row{height:200px;width:120px;background:red}#after{height:70px}");
        Assert.That((v.E("outer").ScrollHeight, v.E("inner").ScrollHeight), Is.EqualTo((170, 200)));
        v.E("outer").ScrollTop = 30; v.E("inner").ScrollTop = 45; v.Update();
        Assert.That(v.Bounds("inner").Y, Is.EqualTo(10)); Assert.That(v.Bounds("row").Y, Is.EqualTo(-35));
        Assert.That(v.Display.HitTest(20, 5)?.Id, Is.Not.EqualTo("row"));
        Assert.That(v.Display.HitTest(20, 20)?.Id, Is.EqualTo("row"));
        Assert.That(v.Display.HitTest(20, 80)?.Id, Is.Not.EqualTo("row"));
        Assert.That(v.E("outer").ScrollHeight, Is.EqualTo(170));
    }
    [TestCase("visible", false)]
    [TestCase("hidden", true)]
    [TestCase("clip", true)]
    public void ExistingOverflowRetainsExactPaintRuns(string overflow, bool clips)
    {
        using var v = new View("<div id=scroller><div id=child></div></div>",
            $"#scroller{{width:100px;height:40px;overflow:{overflow};background:blue}}#child{{height:90px;background:red}}");
        PaintRun[] expected = clips ? new PaintRun[] {
            new BoxRun(new Rect(0,0,400,300), default),
            new BoxRun(new Rect(0,0,100,40), new BoxStyle(new Rgba(0,0,1,1), new Rgba(0,0,0,1),0,0)),
            new BeginRectClipRun(new Rect(0,0,100,40)),
            new BoxRun(new Rect(0,0,100,90), new BoxStyle(new Rgba(1,0,0,1), new Rgba(0,0,0,1),0,0)), new EndRectClipRun()
        } : new PaintRun[] {
            new BoxRun(new Rect(0,0,400,300), default),
            new BoxRun(new Rect(0,0,100,40), new BoxStyle(new Rgba(0,0,1,1), new Rgba(0,0,0,1),0,0)),
            new BoxRun(new Rect(0,0,100,90), new BoxStyle(new Rgba(1,0,0,1), new Rgba(0,0,0,1),0,0))
        };
        Assert.That(v.Display.Runs, Is.EqualTo(expected)); Assert.That(v.Display.Scrollbars, Is.Empty);
        v.E("scroller").ScrollTop = 20; v.Update();
        Assert.That(v.Bounds("child").Y, Is.EqualTo(overflow == "hidden" ? -20 : 0));
    }
    [Test]
    public void FlowRootAndFlexAutoMinimumShrinkScrollViewportAndReclampAfterRemoval()
    {
        using var v = new View("<div id=flex><div id=scroller><div id=content></div></div><div id=footer></div></div>",
            "#flex{display:flex;flex-direction:column;width:140px;height:100px}#scroller{overflow:auto;display:flow-root;flex:1 1 auto}#content{height:200px}#footer{height:20px;flex-shrink:0}");
        Assert.That(v.E("scroller").ClientHeight, Is.EqualTo(80));
        v.E("scroller").ScrollTop = 100; v.E("content").Remove(); v.Update();
        Assert.That(v.E("scroller").ScrollTop, Is.EqualTo(0)); Assert.That(v.Display.Scrollbars, Is.Empty);
    }
    [Test]
    public void PositionedHeldPaintScrollsWithItsActualContainingBlock()
    {
        using var v = new View("<div id=scroller><div id=relative><button id=absolute></button></div><div id=fill></div></div>",
            "#scroller{height:60px;width:100px;overflow:auto;position:relative}#relative{position:relative;height:30px}#absolute{position:absolute;left:5px;top:10px;width:30px;height:20px;background:red}#fill{height:150px}");
        v.E("scroller").ScrollTop = 20; v.Update();
        Assert.That(v.Bounds("absolute"), Is.EqualTo(new Rect(5,-10,30,20)));
        Assert.That(v.Display.Runs.OfType<BoxRun>().Single(b => b.Style.Background.R == 1 && b.Style.Background.G == 0).Bounds, Is.EqualTo(v.Bounds("absolute")));
        Assert.That(v.Display.HitTest(10,5)?.Id, Is.EqualTo("absolute"));
        Assert.That(v.Display.HitTest(10,70)?.Id, Is.Not.EqualTo("absolute"));
    }
    [TestCase("thin", 8)]
    [TestCase("auto", 15)]
    public void ReservedGuttersSettleCrossAxisOverflowAndWithdrawWhenContentFits(string width, double size)
    {
        using var v = new View("<div id=scroller><div id=content></div></div>",
            $"#scroller{{width:100px;height:80px;overflow:auto;scrollbar-width:{width}}}#content{{width:96px;height:100px}}");
        Assert.That((v.E("scroller").ClientWidth,v.E("scroller").ClientHeight),
            Is.EqualTo(((int)(100-size),(int)(80-size))));
        Assert.That(v.Display.Scrollbars.Count, Is.EqualTo(2));
        v.E("content").Remove(); v.Update();
        Assert.That((v.E("scroller").ClientWidth,v.E("scroller").ClientHeight), Is.EqualTo((100,80)));
        Assert.That(v.Display.Scrollbars, Is.Empty);
    }
    [Test]
    public void FlexColumnAutoGutterMeasuresUsedItemsBeyondTheDefiniteViewport()
    {
        using var v = new View("<div id=scroller><button></button><button></button><button></button><button></button></div>",
            "#scroller{display:flex;flex-direction:column;width:220px;height:100px;overflow-y:auto;overflow-x:hidden;scrollbar-width:auto;--necronomidom-scrollbar-size:8px}button{height:40px;flex-shrink:0}");
        Assert.That((v.E("scroller").ClientWidth, v.E("scroller").ClientHeight, v.E("scroller").ScrollHeight), Is.EqualTo((212,100,160)));
        Assert.That(v.Display.Scrollbars.Count, Is.EqualTo(1));
        Assert.That(v.Display.Boxes.Where(b => b.Element.LocalName == "button").Select(b => b.Bounds.Width), Is.All.EqualTo(212));
    }
}
