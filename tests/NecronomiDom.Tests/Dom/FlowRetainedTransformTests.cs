using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class FlowRetainedTransformTests
{
    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }
    private static (Document Doc, StyleEngine Styles, FlowLayout Flow, HTMLElement Target) Load()
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;" +
            "font-family:monospace;font-size:16px;line-height:20px;appearance:none}html,body{width:100%;height:100%}" +
            "#p{position:absolute;left:40px;bottom:10px;width:100px;height:80px;overflow:hidden;background-color:red}" +
            "#c{width:120px;height:30px;background-color:blue}</style>" +
            "<div id=p style='transform:rotate(0deg);translate:0px;scale:1'><div id=c></div></div>",
            "retained-transform", WebUrl.Parse("res:///retained-transform.html")!).Document;
        var styles = new StyleEngine(doc);
        return (doc, styles, new FlowLayout(styles, new Metrics(), "monospace"), (HTMLElement)doc.GetElementById("p")!);
    }

    [TestCase("transform", "translate(10px,5px) rotate(12deg)")]
    [TestCase("translate", "15% 12px")]
    [TestCase("scale", ".75 1.2")]
    [TestCase("rotate", "45deg")]
    public void ExistingCarrierMatchesFullPaintAndTransformedClipHits(string property, string value)
    {
        var (doc, styles, flow, target) = Load();
        var list = flow.Layout(doc, 240, 160);
        // Adding an inline longhand changes declaration membership and must first establish the new snapshot.
        if (property == "rotate") { target.Style.SetProperty(property, "0deg"); list = flow.Layout(doc, 240, 160); }
        target.Style.SetProperty(property, value);
        Assert.That(styles.TryConsumeInlineTransformChange(target), Is.True);
        Assert.That(flow.TryPrepareTransformUpdate(target, out int ordinal, out var transform), Is.True);
        Assert.That(ordinal, Is.EqualTo(0));
        flow.ApplyTransformUpdate(target, transform);
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
        if (property == "rotate") referenceTarget.Style.SetProperty(property, "0deg");
        referenceTarget.Style.SetProperty(property, value);
        referenceStyles.Invalidate();
        var expected = referenceFlow.Layout(referenceDoc, 240, 160);
        Assert.That(list.Runs, Is.EqualTo(expected.Runs));
        Assert.That(list.Boxes.Select(box => box.Bounds), Is.EqualTo(expected.Boxes.Select(box => box.Bounds)));
        for (int y = 0; y < 160; y += 4)
            for (int x = 0; x < 240; x += 4)
                Assert.That(list.HitTest(x, y)?.Id, Is.EqualTo(expected.HitTest(x, y)?.Id), $"{x},{y}");
    }

    [Test]
    public void RemovingTheOnlyTransformCannotKeepAnObsoleteContainingBlock()
    {
        var (doc, styles, flow, target) = Load();
        target.Style.RemoveProperty("translate"); target.Style.RemoveProperty("scale");
        flow.Layout(doc, 240, 160);
        target.Style.SetProperty("transform", "none");
        Assert.That(styles.TryConsumeInlineTransformChange(target), Is.True);
        Assert.That(flow.TryPrepareTransformUpdate(target, out _, out _), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedUpdatesMatchFullHitsAndKeepUnrelatedBoxSnapshots(bool childFirst)
    {
        static HTMLElement AddNestedContent(Document doc)
        {
            var child = (HTMLElement)doc.GetElementById("c")!;
            child.Style.SetProperty("transform", "translate(0px)");
            var absolute = (HTMLElement)doc.CreateElement("div");
            absolute.Id = "absolute";
            absolute.Style.CssText = "position:absolute;left:70px;top:0;width:80px;height:20px;background:red";
            child.AppendChild(absolute);
            return child;
        }
        var (doc, styles, flow, target) = Load();
        HTMLElement child = AddNestedContent(doc);
        var list = flow.Layout(doc, 240, 160);
        var unchanged = list.Boxes.Where(box => !target.Contains(box.Element)).ToArray();
        Assert.That(unchanged.Length, Is.GreaterThan(0));
        target.Style.SetProperty("transform", "rotate(17deg)");
        child.Style.SetProperty("transform", "translate(14px,3px)");
        foreach (var element in childFirst ? new[] { child, target } : new[] { target, child })
        {
            Assert.That(styles.TryConsumeInlineTransformChange(element), Is.True);
            Assert.That(flow.TryPrepareTransformUpdate(element, out _, out var transform), Is.True);
            flow.ApplyTransformUpdate(element, transform);
        }
        foreach (var before in unchanged)
            Assert.That(list.Boxes.Single(box => box.Element == before.Element), Is.SameAs(before));
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
        HTMLElement referenceChild = AddNestedContent(referenceDoc);
        referenceTarget.Style.SetProperty("transform", "rotate(17deg)");
        referenceChild.Style.SetProperty("transform", "translate(14px,3px)");
        referenceStyles.Invalidate();
        var expected = referenceFlow.Layout(referenceDoc, 240, 160);
        Assert.That(list.Runs, Is.EqualTo(expected.Runs));
        Assert.That(list.Boxes.Select(box => box.VisualBounds), Is.EqualTo(expected.Boxes.Select(box => box.VisualBounds)));
        for (int y = 0; y < 160; y += 4)
            for (int x = 0; x < 240; x += 4)
                Assert.That(list.HitTest(x, y)?.Id, Is.EqualTo(expected.HitTest(x, y)?.Id), $"{x},{y}");
    }
}
