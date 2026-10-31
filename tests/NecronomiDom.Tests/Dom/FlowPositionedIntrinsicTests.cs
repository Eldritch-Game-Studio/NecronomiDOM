using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    [Test]
    public void PositionedNestedBlocksUseRecursiveTextAndTheirActualBoxEdges()
    {
        var (doc, input, display) = Fixture("<div id=outer><div id=middle><div id=leaf>ABCDE</div></div></div>", """
            #outer{position:absolute;left:0;top:0;width:auto}
            #middle{padding:2px;border:1px solid black}
            #leaf{padding:3px;border:2px solid black}
            """);
        using var owner = input;
        Assert.That((Bounds(display, "outer").Width, Bounds(display, "outer").Height), Is.EqualTo((56d, 36d)));
        Assert.That(display.Runs.OfType<TextRun>().Single().X, Is.EqualTo(8));
        Assert.That(Click(doc, input, 8, 8), Is.EqualTo("leaf"));
    }
    [Test]
    public void PositionedNestedRowsSumActualChildEdgesMarginsAndGap()
    {
        var (_, input, display) = Fixture("<div id=outer><div id=a>AA</div><div id=b>BBB</div></div>", """
            #outer{position:absolute;left:0;top:0;width:auto;display:flex;gap:5px}
            #a{padding:2px;border:1px solid black;margin-left:3px;margin-right:4px}
            #b{padding:1px;margin-left:2px}
            """);
        using var owner = input;
        Assert.That(Bounds(display, "outer").Width, Is.EqualTo(62));
        Assert.That(Bounds(display, "a").Width, Is.EqualTo(22));
        Assert.That(Bounds(display, "b").Width, Is.EqualTo(26));
        Assert.That(Bounds(display, "b").X, Is.EqualTo(36));
    }
    [Test]
    public void PositionedNestedReplacedContentUsesTheExistingRealImageIntrinsicSource()
    {
        var (doc, input, display) = Fixture("<div id=outer><div id=middle><img id=picture></div></div>", """
            #outer{position:absolute;left:10px;top:10px;width:auto;padding:4px;border:1px solid black}
            #middle{padding:3px;border:2px solid black}
            """, new IntrinsicImage());
        using var owner = input;
        Assert.That((Bounds(display, "outer").Width, Bounds(display, "outer").Height), Is.EqualTo((60d, 40d)));
        Assert.That(Bounds(display, "picture"), Is.EqualTo(new Rect(20, 20, 40, 20)));
        Assert.That(Click(doc, input, 21, 21), Is.EqualTo("picture"));
    }
    [Test]
    public void HiddenAndOutOfFlowNestedChildrenDoNotContributeToShrinkToFitWidth()
    {
        var (_, input, display) = Fixture("<div id=outer><div id=middle><div>AA</div><div id=hidden></div><div id=floating></div></div></div>", """
            #outer{position:absolute;left:0;top:0;width:auto}
            #hidden{display:none;width:999px}
            #floating{position:absolute;left:0;top:0;width:900px;height:2px}
            """);
        using var owner = input;
        Assert.That(Bounds(display, "outer").Width, Is.EqualTo(16));
    }
    [Test]
    public void ExistingDirectPercentageShrinkToFitRejectionRemainsExplicit()
    {
        Assert.That(() => Fixture("<div id=outer><div id=child></div></div>",
            "#outer{position:absolute;left:0;top:0;width:auto}#child{width:50%}"),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("percentage child width"));
    }
    [Test]
    public void DynamicHealthPipsContributeTheirOuterWidthsToThePositionedHudRow()
    {
        // Script-created anonymous div.hp-pip children are added after the first layout.
        // The pure spy supplies deterministic advances; it is not font/raster evidence.
        var doc = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div,p{display:block;margin:0;padding:0;border:0 solid black;box-sizing:border-box;font-family:monospace;font-size:18px;line-height:normal;appearance:none}" +
            "html,body{width:100%;height:100%;background:white}body,div{display:flex;flex-direction:column;position:relative;flex-shrink:0}" +
            "#stage{position:relative;width:300px;height:100px}" +
            ".hud-row{position:absolute;left:18px;top:14px;display:flex;flex-direction:row;align-items:center;padding:6px 12px;border:2px solid black;width:auto}" +
            ".hp-row{display:flex;flex-direction:row;align-items:center}" +
            ".hp-pip{width:18px;height:18px;padding:0;border:2px solid black;box-sizing:border-box;margin-right:4px}" +
            ".hp-pip-spent{background:#123456}" +
            "#count{margin-left:8px;font-size:30px}#glyph{width:72px;height:72px;margin-left:8px}" +
            "</style><div id=stage><div id=hud class=hud-row><div id=hp class=hp-row></div><p id=count>0 / 0</p><div id=glyph></div></div></div>",
            "pips-intrinsic", WebUrl.Parse("res:///pips-intrinsic.html")!).Document;
        var styles = new StyleEngine(doc);
        var flow = new FlowLayout(styles, new FixedGlyphMetrics(), "monospace");
        var initial = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(initial, "hud").Width, Is.EqualTo(156));
        var hp = doc.GetElementById("hp")!;
        for (int i = 0; i < 3; i++)
        {
            var pip = doc.CreateElement("div");
            pip.SetAttribute("class", "hp-pip");
            hp.AppendChild(pip);
        }
        styles.Invalidate();
        var active = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(active, "hud"), Is.EqualTo(new Rect(18, 14, 222, 88)));
        Assert.That(Bounds(active, "hp").Width, Is.EqualTo(66));
        Assert.That(Bounds(active, "glyph").X, Is.EqualTo(154));
        Assert.That(hp.ChildNodes.Count, Is.EqualTo(3));
        hp.FirstElementChild!.SetAttribute("class", "hp-pip hp-pip-spent");
        styles.Invalidate();
        var spent = flow.Layout(doc, 400, 300);
        Assert.That(Bounds(spent, "hud"), Is.EqualTo(Bounds(active, "hud")));
        Assert.That(hp.ChildNodes.Count, Is.EqualTo(3));
    }
}
