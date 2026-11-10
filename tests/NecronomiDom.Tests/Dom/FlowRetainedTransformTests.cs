using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.TypedOm;
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
    [TestCase("transform", "scaleY(0.42)")]
    [TestCase("transform", "translateY(-40px)")]
    [TestCase("transform", "scaleX(.5) skewY(3deg) translateX(4px)")]
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
    public void ReusedTypedTransformUpdatesPaintAndClippedHitsOnEveryMutation()
    {
        var (doc, styles, flow, target) = Load();
        var components = new[] { CSSTransformComponent.Translate(new CSSUnitValue(0, "percent"), new CSSUnitValue(5, "px")) };
        var value = new CSSTransformValue(components);
        target.AttributeStyleMap.Set("transform", value);
        var list = flow.Layout(doc, 240, 160);
        foreach (int percent in new[] { 15, 55, -10 })
        {
            components[0] = CSSTransformComponent.Translate(new CSSUnitValue(percent, "percent"), new CSSUnitValue(5, "px"));
            target.AttributeStyleMap.Set("transform", value);
            Assert.That(styles.TryConsumeInlineTransformChange(target), Is.True);
            Assert.That(flow.TryPrepareTransformUpdate(target, out _, out var transform), Is.True);
            flow.ApplyTransformUpdate(target, transform);

            var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
            referenceTarget.Style.SetProperty("transform", $"translate({percent}%,5px)");
            referenceStyles.Invalidate();
            var expected = referenceFlow.Layout(referenceDoc, 240, 160);
            Assert.That(list.Runs, Is.EqualTo(expected.Runs));
            for (int y = 0; y < 160; y += 4)
                for (int x = 0; x < 240; x += 4)
                    Assert.That(list.HitTest(x, y)?.Id, Is.EqualTo(expected.HitTest(x, y)?.Id), $"{percent}: {x},{y}");
        }
    }

    [Test]
    public void TypedRelativeTranslationRetainsFontDependentLayout()
    {
        var (doc, styles, flow, target) = Load();
        var components = new[] { CSSTransformComponent.Translate(new CSSUnitValue(0, "em"), new CSSUnitValue(0, "px")) };
        var value = new CSSTransformValue(components);
        target.AttributeStyleMap.Set("transform", value);
        flow.Layout(doc, 240, 160);
        components[0] = CSSTransformComponent.Translate(new CSSUnitValue(2, "em"), new CSSUnitValue(0, "px"));
        target.AttributeStyleMap.Set("transform", value);
        Assert.That(styles.TryConsumeInlineTransformChange(target), Is.False);
        var actual = flow.Layout(doc, 240, 160);
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
        referenceTarget.Style.SetProperty("transform", "translate(2em,0px)");
        referenceStyles.Invalidate();
        Assert.That(actual.Runs, Is.EqualTo(referenceFlow.Layout(referenceDoc, 240, 160).Runs));
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

    [TestCase("scaleY(0.3)", "scaleY(0.42)")]
    [TestCase("scaleY(0)", "scaley(0.5)")]
    [TestCase("translateY(-40px)", "translateY(-50px)")]
    public void MeterBarInlineTransformWritesAreCompositorOnly(string initial, string next)
    {
        // Dirge's mixer drives absolutely positioned meter bars through inline scaleY/translateY writes.
        static Document Meter(string transform) => HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;" +
            "font-family:monospace;font-size:16px;line-height:20px;appearance:none}html,body{width:100%;height:100%}" +
            "#meter{position:relative;width:24px;height:148px;background-color:#111}" +
            "#bar{position:absolute;bottom:0;left:2px;width:8px;height:148px;background-color:#3fb950;transform-origin:50% 100%}</style>" +
            "<div id=meter><div id=bar style='transform:" + transform + "'></div></div>",
            "meter-transform", WebUrl.Parse("res:///meter-transform.html")!).Document;
        var doc = Meter(initial);
        var styles = new StyleEngine(doc);
        var flow = new FlowLayout(styles, new Metrics(), "monospace");
        var bar = (HTMLElement)doc.GetElementById("bar")!;
        var list = flow.Layout(doc, 240, 160);
        long computed = styles.ComputedEntryCount, refreshes = styles.GeometryRefreshCount;
        bar.Style.SetProperty("transform", next);
        Assert.That(styles.TryConsumeInlineTransformChange(bar), Is.True);
        Assert.That(flow.TryPrepareTransformUpdate(bar, out int ordinal, out var transform), Is.True);
        Assert.That(ordinal, Is.EqualTo(0));
        flow.ApplyTransformUpdate(bar, transform);
        Assert.That(styles.GeometryRefreshCount, Is.EqualTo(refreshes + 1));
        Assert.That(styles.ComputedEntryCount - computed, Is.EqualTo(1), "the only refresh was the geometry-only one, not a full cascade");
        var referenceDoc = Meter(next);
        var referenceStyles = new StyleEngine(referenceDoc);
        var expected = new FlowLayout(referenceStyles, new Metrics(), "monospace").Layout(referenceDoc, 240, 160);
        Assert.That(list.Runs, Is.EqualTo(expected.Runs));
        Assert.That(list.Boxes.Select(box => box.VisualBounds), Is.EqualTo(expected.Boxes.Select(box => box.VisualBounds)));
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
