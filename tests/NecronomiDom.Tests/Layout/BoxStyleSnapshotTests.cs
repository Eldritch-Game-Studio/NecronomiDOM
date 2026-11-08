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
public sealed class BoxStyleSnapshotTests
{
    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }

    private const string Css = "html,body,div,p{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;" +
        "font-family:monospace;font-size:16px;line-height:20px;appearance:none}html,body{width:100%;height:100%}" +
        ".row{display:flex;align-items:flex-start;padding:4px}.item{flex:1 1 0px;height:30px;background-color:#123456;border:2px solid red;border-radius:5px}";

    private static (Document Doc, StyleEngine Styles, FlowLayout Flow) Load(string body)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" + Css + "</style>" + body, "snapshot", WebUrl.Parse("res:///snapshot.html")!).Document;
        var styles = new StyleEngine(doc);
        return (doc, styles, new FlowLayout(styles, new Metrics(), "monospace"));
    }
    private static Rect Bounds(DisplayList display, string id) => display.Boxes.Single(b => b.Element.Id == id).Bounds;

    [Test]
    public void UnchangedStyleKeepsOneSnapshotAcrossLayoutsAndUnrelatedMutations()
    {
        var (doc, styles, flow) = Load("<div class=row><div class=item id=a></div><div class=item id=b><p id=t>x</p></div></div>");
        flow.Layout(doc, 300, 200);
        var a = doc.GetElementById("a")!;
        var first = BoxStyleSnapshot.For(styles.GetComputedStyle(a));
        Assert.That(first.PaddingTop.Kind, Is.EqualTo(BoxLengthKind.Px));
        Assert.That(first.BorderTopWidth.Value, Is.EqualTo(2));
        Assert.That(first.Mode, Is.EqualTo(PositionedMode.Static));
        Assert.That(first.FlexGrow, Is.EqualTo(1));
        Assert.That(first.Background, Is.EqualTo(new Rgba(0x12 / 255f, 0x34 / 255f, 0x56 / 255f, 1)));
        // A text change elsewhere restyles that subtree only; a re-cascade with identical values keeps the view.
        doc.GetElementById("t")!.TextContent = "yy";
        flow.Layout(doc, 300, 200);
        Assert.That(BoxStyleSnapshot.For(styles.GetComputedStyle(a)), Is.SameAs(first));
        var itemB = doc.GetElementById("b")!;
        var beforeB = BoxStyleSnapshot.For(styles.GetComputedStyle(itemB));
        itemB.FirstElementChild!.TextContent = "zzz";
        flow.Layout(doc, 300, 200);
        Assert.That(BoxStyleSnapshot.For(styles.GetComputedStyle(itemB)), Is.SameAs(beforeB));
    }

    [Test]
    public void InlineStyleChangeRefreshesTheSnapshotAndTheLaidOutGeometry()
    {
        var (doc, styles, flow) = Load("<div class=row><div class=item id=a></div><div class=item id=b></div></div>");
        var before = flow.Layout(doc, 300, 200);
        var a = (HTMLElement)doc.GetElementById("a")!;
        var stale = BoxStyleSnapshot.For(styles.GetComputedStyle(a));
        Assert.That(Bounds(before, "a"), Is.EqualTo(new Rect(4, 4, 150 - 4, 34)));
        a.Style.SetProperty("padding-left", "10px");
        a.Style.SetProperty("flex-grow", "3");
        a.Style.SetProperty("border-top-color", "blue");
        var fresh = BoxStyleSnapshot.For(styles.GetComputedStyle(a));
        Assert.That(fresh, Is.Not.SameAs(stale));
        Assert.That(fresh.PaddingLeft.Value, Is.EqualTo(10));
        Assert.That(fresh.FlexGrow, Is.EqualTo(3));
        Assert.That(fresh.Borders.Top.Color, Is.EqualTo(new Rgba(0, 0, 1, 1)));
        Assert.That(stale.Borders.Top.Color, Is.EqualTo(new Rgba(1, 0, 0, 1)), "the stale view is not mutated in place");
        var after = flow.Layout(doc, 300, 200);
        // flex: 1 1 0px with grow 3:1 over 274px of free space → 219.5 : 72.5 outer widths.
        Assert.That(Bounds(after, "a").Width, Is.EqualTo(219.5));
        Assert.That(Bounds(after, "b").Width, Is.EqualTo(72.5));
        Assert.That(after.Runs.OfType<BorderBoxRun>().Single(r => r.Bounds == Bounds(after, "a")).Borders.Top.Color, Is.EqualTo(new Rgba(0, 0, 1, 1)));
        var (referenceDoc, _, referenceFlow) = Load("<div class=row><div class=item id=a style='padding-left:10px;flex-grow:3;border-top-color:blue'></div><div class=item id=b></div></div>");
        var expected = referenceFlow.Layout(referenceDoc, 300, 200);
        Assert.That(after.Runs, Is.EqualTo(expected.Runs));
        Assert.That(after.Boxes.Select(b => b.Bounds), Is.EqualTo(expected.Boxes.Select(b => b.Bounds)));
    }

    [Test]
    public void StylesheetChangeRefreshesEveryAffectedSnapshot()
    {
        var (doc, styles, flow) = Load("<div class=row><div class=item id=a></div><div class=item id=b></div></div>");
        flow.Layout(doc, 300, 200);
        var a = doc.GetElementById("a")!;
        var stale = BoxStyleSnapshot.For(styles.GetComputedStyle(a));
        var sheet = doc.StyleSheets[0];
        sheet.InsertRule(".item{height:50px;border-width:0}", sheet.CssRules.Count);
        var fresh = BoxStyleSnapshot.For(styles.GetComputedStyle(a));
        Assert.That(fresh, Is.Not.SameAs(stale));
        Assert.That(fresh.Height.Value, Is.EqualTo(50));
        Assert.That(fresh.BorderTopWidth.Value, Is.EqualTo(0));
        var after = flow.Layout(doc, 300, 200);
        Assert.That(Bounds(after, "a").Height, Is.EqualTo(50));
        Assert.That(Bounds(after, "b").Height, Is.EqualTo(50));
    }

    [Test]
    public void UnsupportedValuesAreRejectedOnlyByThePathThatReadsThem()
    {
        // A hidden element with an unsupported position must still lay out as nothing; the snapshot defers the rejection.
        var (doc, styles, flow) = Load("<div class=row><div class=item id=a style='display:none;position:fixed'></div><div class=item id=b></div></div>");
        var display = flow.Layout(doc, 300, 200);
        Assert.That(display.Boxes.Any(b => b.Element.Id == "a"), Is.False);
        var snapshot = BoxStyleSnapshot.For(styles.GetComputedStyle(doc.GetElementById("a")!));
        Assert.That(snapshot.Mode, Is.Null);
        var ex = Assert.Throws<NotSupportedException>(() => PositionedLayoutSolver.Prepare(styles.GetComputedStyle(doc.GetElementById("a")!),
            new(new Rect(0, 0, 100, 100), new Rect(0, 0, 100, 100), true), new(new Rect(0, 0, 100, 100), new Rect(0, 0, 100, 100), true), 0, 0, default));
        Assert.That(ex!.Message, Is.EqualTo("Positioned layout rejects position: fixed"));
    }
}
