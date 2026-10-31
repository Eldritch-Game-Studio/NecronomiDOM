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
public sealed partial class FlowPositionedInputTests
{
    private sealed class FixedGlyphMetrics : ITextShaper
    { public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }
    private sealed class IntrinsicImage : IImageSource
    { public ImageResource Load(Element element) => new("memory:40x20", 40, 20); }
    private static (Document Doc, DocumentInput Input, DisplayList Display) Fixture(string body, string css, IImageSource? images = null)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div,button,img{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;font-family:monospace;font-size:16px;line-height:20px;appearance:none}" +
            "html,body{width:100%;height:100%;background-color:white}" + css + "</style>" + body,
            "positioned", WebUrl.Parse("res:///positioned.html")!).Document;
        var styles = new StyleEngine(doc);
        var display = new FlowLayout(styles, new FixedGlyphMetrics(), "monospace", images).Layout(doc, 400, 300);
        var input = new DocumentInput(doc, styles); input.UpdateGeometry(display, 400, 300);
        return (doc, input, display);
    }
    private static Rect Bounds(DisplayList display, string id) => display.Boxes.Single(box => box.Element.Id == id).Bounds;
    private static string Click(Document doc, DocumentInput input, double x, double y)
    {
        string target = "";
        var listener = new DelegateEventListener(e => target = (e.Target as Element)?.Id ?? "");
        doc.AddEventListener("pointerdown", listener, true);
        try { input.PointerMove(x, y); input.PointerButton(x, y, true); input.PointerButton(x, y, false); }
        finally { doc.RemoveEventListener("pointerdown", listener, true); }
        return target;
    }
    [Test]
    public void AbsoluteUsesNearestFinalPaddingBoxAndEscapesIntermediateStaticOverflow()
    {
        var (doc, input, display) = Fixture("<div id=parent><div id=wrapper><div id=absolute></div></div></div>", """
            #parent{position:relative;width:200px;height:100px;padding:10px;border:2px solid black;overflow:hidden}
            #wrapper{width:20px;height:20px;overflow:hidden}
            #absolute{position:absolute;left:50%;top:50%;width:50px;height:20px;background-color:green}
            """);
        using var owner = input;
        var bounds = Bounds(display, "absolute");
        Assert.That(bounds.X, Is.EqualTo(112d).Within(1).Ulps);
        Assert.That((bounds.Y, bounds.Width, bounds.Height), Is.EqualTo((62d, 50d, 20d)));
        Assert.That(Click(doc, input, 120, 70), Is.EqualTo("absolute"));
        Assert.That(display.Runs.OfType<BeginRectClipRun>().Select(run => run.Bounds), Does.Contain(new Rect(12, 12, 20, 20)));
    }
    [TestCase(1, 1, "")]
    [TestCase(2, 2, "parent")]
    [TestCase(2.1, 2.1, "parent")]
    [TestCase(2.5, 2.5, "parent")]
    [TestCase(3, 3, "fill")]
    [TestCase(3, 2, "parent")]
    [TestCase(2, 3, "parent")]
    public void RoundedOverflowUsesTheActualBrowserHitPixelAndPreservesTheBorderRing(double x, double y, string target)
    {
        var (doc, input, display) = Fixture("<div id=parent><div id=fill></div></div>", """
            #parent{position:relative;width:100px;height:100px;border:1px solid black;border-radius:10px;overflow:hidden;background-color:blue}
            #fill{position:absolute;inset:0;width:100%;height:100%;background-color:green}
            """);
        using var owner = input;
        Assert.That(new RoundedRectGeometry(Bounds(display, "parent"), 10).Contains(2, 2), Is.False, "paint geometry is not inflated");
        Assert.That(Click(doc, input, x, y), Is.EqualTo(target), "actual trusted Chrome query-quad boundary");
        Assert.That(Click(doc, input, 12, 12), Is.EqualTo("fill"));
    }
    [Test]
    public void PolygonMasksActualDescendantHitAndEmitsTrueFilledPolygonAndSubtreeContext()
    {
        var (doc, input, display) = Fixture("<div id=polygon><div id=child></div></div>", """
            #polygon{position:relative;width:100px;height:100px;clip-path:polygon(0px 0px,100px 0px,0px 100px);background-color:blue}
            #child{position:absolute;inset:0;background-color:green}
            """);
        using var owner = input;
        Assert.That(Click(doc, input, 10, 10), Is.EqualTo("child"));
        Assert.That(Click(doc, input, 90, 90), Is.Not.EqualTo("child"));
        Assert.That(display.Runs.OfType<PolygonRun>().Count(), Is.EqualTo(1));
        Assert.That(display.Runs.OfType<BeginPolygonClipRun>().Count(), Is.EqualTo(1));
        Assert.That(display.Runs.OfType<EndPolygonClipRun>().Count(), Is.EqualTo(1));
    }
    [Test]
    public void DefinitePercentHeightAndIndefiniteAutoHeightUseActualFlowAncestors()
    {
        var (_, input, display) = Fixture("<div id=definite><div id=half></div></div><div id=auto><div id=indefinite><div id=natural></div></div></div>", """
            #definite{width:100px;height:80px}#half{height:50%}
            #auto{width:100px}#indefinite{height:50%}#natural{height:24px}
            """);
        using var owner = input;
        Assert.That(Bounds(display, "half").Height, Is.EqualTo(40));
        Assert.That(Bounds(display, "indefinite").Height, Is.EqualTo(24));
    }
    [Test]
    public void BottomAnchoredNaturalHeightMovesExistingChildPaintAndGeometryTogether()
    {
        var (doc, input, display) = Fixture("<div id=parent><div id=absolute><div id=child></div></div></div>", """
            #parent{position:relative;width:200px;height:200px}
            #absolute{position:absolute;bottom:10px;width:50px}#child{width:50px;height:30px;background-color:green}
            """);
        using var owner = input;
        Assert.That(Bounds(display, "absolute"), Is.EqualTo(new Rect(0, 160, 50, 30)));
        Assert.That(Bounds(display, "child"), Is.EqualTo(new Rect(0, 160, 50, 30)));
        Assert.That(display.Runs.OfType<BoxRun>().Any(run => run.Bounds == new Rect(0, 160, 50, 30)), Is.True);
        Assert.That(Click(doc, input, 10, 170), Is.EqualTo("child"));
    }
    [Test]
    public void OpacityIsARealPaintGroupAndDoesNotRemoveDocumentHitEligibility()
    {
        var (doc, input, display) = Fixture("<div id=group><div id=child></div></div>", """
            #group{position:relative;width:100px;height:100px;opacity:0}
            #child{position:absolute;inset:0;background-color:green}
            """);
        using var owner = input;
        Assert.That(display.Runs.OfType<BeginOpacityRun>().Single().Opacity, Is.Zero);
        Assert.That(display.Runs.OfType<EndOpacityRun>().Count(), Is.EqualTo(1));
        Assert.That(input.HasPoint(10, 10), Is.True);
        Assert.That(Click(doc, input, 10, 10), Is.EqualTo("child"));
    }
    [Test]
    public void TabindexOrderRemainsDomTreeOrderWhenAbsolutePaintIsDeferred()
    {
        var (doc, input, display) = Fixture("<div id=parent><button id=first>A</button><button id=second>B</button></div>", """
            #parent{position:relative;width:200px;height:100px}button{width:40px;height:20px}
            #first{position:absolute;left:100px;top:50px}
            """);
        using var owner = input;
        Assert.That(display.Boxes.FindIndex(box => box.Element.Id == "first"), Is.GreaterThan(display.Boxes.FindIndex(box => box.Element.Id == "second")));
        Assert.That(input.MoveSequential(false), Is.True); Assert.That(doc.FocusedElement?.Id, Is.EqualTo("first"));
        Assert.That(input.MoveSequential(false), Is.True); Assert.That(doc.FocusedElement?.Id, Is.EqualTo("second"));
    }
    [Test]
    public void ExistingReplacedImageAutoSizeAndAutoMarginsRemainIntrinsic()
    {
        var (_, input, display) = Fixture("<div id=parent><img id=image src=memory></div>",
            "#parent{width:200px}#image{margin-left:auto;margin-right:auto}", new IntrinsicImage());
        using var owner = input;
        Assert.That(Bounds(display, "image"), Is.EqualTo(new Rect(80, 0, 40, 20)));
        Assert.That(display.Runs.OfType<ImageRun>().Single().Destination, Is.EqualTo(new Rect(80, 0, 40, 20)));
    }
    [Test]
    public void PartialBackgroundAlphaIsNotFlattenedIntoDescendantOrGroupAlpha()
    {
        var (_, input, display) = Fixture("<div id=group><div id=child></div></div>", """
            #group{position:relative;width:100px;height:100px;opacity:0.5;background-color:rgba(255,0,0,0.5)}
            #child{position:absolute;left:10px;top:10px;width:20px;height:20px;background-color:blue}
            """);
        using var owner = input;
        Assert.That(display.Runs.OfType<BeginOpacityRun>().Single().Opacity, Is.EqualTo(0.5f));
        Assert.That(display.Runs.OfType<BoxRun>().Single(run => run.Bounds == Bounds(display, "group")).Style.Background.A, Is.EqualTo(0.5f));
        Assert.That(display.Runs.OfType<BoxRun>().Single(run => run.Bounds == Bounds(display, "child")).Style.Background.A, Is.EqualTo(1));
    }
    [TestCase("visible", "fill")]
    [TestCase("hidden", "")]
    [TestCase("clip", "")]
    public void OwnOuterRoundClipDoesNotBecomeAnImplicitDescendantOverflowClip(string overflow, string cornerTarget)
    {
        var (doc, input, _) = Fixture("<div id=round><div id=fill></div></div>",
            $"#round{{width:40px;height:40px;border-radius:20px;overflow:{overflow}}}#fill{{width:40px;height:40px}}");
        using var owner = input;
        Assert.That(Click(doc, input, 1, 1), Is.EqualTo(cornerTarget));
        Assert.That(Click(doc, input, 10, 10), Is.EqualTo("fill"));
    }
    [TestCase("visible")]
    [TestCase("hidden")]
    [TestCase("clip")]
    public void RoundedButtonsAndReplacedImagesUseTheirOwnCurvedBorderHitRegardlessOfOverflow(string overflow)
    {
        var (doc, input, _) = Fixture("<button id=button></button><img id=image src='memory:test'>",
            $"#button,#image{{width:40px;height:40px;border-radius:20px;overflow:{overflow}}}", new IntrinsicImage());
        using var owner = input;
        Assert.That(Click(doc, input, 1, 1), Is.Empty);
        Assert.That(Click(doc, input, 10, 10), Is.EqualTo("button"));
        Assert.That(Click(doc, input, 1, 41), Is.Empty);
        Assert.That(Click(doc, input, 10, 50), Is.EqualTo("image"));
    }
    [Test]
    public void RelativeVisualOffsetDoesNotMoveFollowingNormalFlow()
    {
        var (_, input, display) = Fixture("<div id=relative></div><div id=following></div>",
            "#relative{position:relative;left:10px;top:15px;width:50px;height:20px}#following{width:50px;height:30px}");
        using var owner = input;
        Assert.That(Bounds(display, "relative"), Is.EqualTo(new Rect(10, 15, 50, 20)));
        Assert.That(Bounds(display, "following"), Is.EqualTo(new Rect(0, 20, 50, 30)));
    }
}
