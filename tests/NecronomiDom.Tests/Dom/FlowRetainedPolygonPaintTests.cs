using System;
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
public sealed class FlowRetainedPolygonPaintTests
{
    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }

    private static (Document Doc, StyleEngine Styles, FlowLayout Flow, HTMLElement Target) Load(string css = "", string child = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html,body,div,span{display:block;margin:0;padding:0;" +
            "font-family:monospace;font-size:16px;line-height:20px;appearance:none}html,body{width:100%;height:100%}" +
            "#outer{position:absolute;left:25px;top:15px;width:170px;height:120px;overflow:hidden;transform:rotate(7deg)}" +
            "#p{position:absolute;left:10px;top:10px;width:100px;height:80px}" +
            "#other{position:absolute;left:2px;top:2px;width:30px;height:30px;background:blue}" + css +
            "</style><div id=outer><div id=p style='clip-path:polygon(0% 0%,90% 0%,90% 90%);background-color:rgba(20,40,60,.7);left:10px'>" +
            child + "</div><div id=other></div></div>", "retained-polygon", WebUrl.Parse("res:///retained-polygon.html")!).Document;
        var styles = new StyleEngine(doc) { IncrementalReconciliation = true };
        return (doc, styles, new FlowLayout(styles, new Metrics(), "monospace"), (HTMLElement)doc.GetElementById("p")!);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedClipAndPaintMatchFullLayoutAndHitsWithoutReplacingUnrelatedBoxes(bool typedPaint)
    {
        var (doc, styles, flow, target) = Load();
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
        var list = flow.Layout(doc, 240, 160);
        var unrelated = list.Boxes.Where(box => box.Element != target).ToArray();
        for (int frame = 0; frame < 20; frame++)
        {
            string clip = FormattableString.Invariant($"polygon(0px 0px,{30 + frame}% 2px,90% 90%)");
            string color = FormattableString.Invariant($"rgba({30 + frame},70,120,.73)");
            target.Style.SetProperty("clip-path", clip);
            referenceTarget.Style.SetProperty("clip-path", clip);
            if (typedPaint)
            {
                var sample = CSSStyleValue.Parse("background-color", color);
                target.AttributeStyleMap.Set("background-color", sample);
                referenceTarget.AttributeStyleMap.Set("background-color", sample);
            }
            else
            {
                target.Style.SetProperty("background-color", color);
                referenceTarget.Style.SetProperty("background-color", color);
            }
            Assert.That(styles.TryConsumeInlinePolygonPaintChange(target), Is.True);
            Assert.That(styles.TryConsumeInlinePolygonPaintChange(target), Is.False, "A delta is consumed once.");
            Assert.That(flow.TryPreparePolygonPaintUpdate(target, out var update), Is.True);
            Assert.That(flow.ApplyPolygonPaintUpdates(new[] { update }), Is.True);
            foreach (var before in unrelated)
                Assert.That(list.Boxes.Single(box => box.Element == before.Element), Is.SameAs(before));
            referenceStyles.Invalidate();
            var expected = referenceFlow.Layout(referenceDoc, 240, 160);
            Assert.That(list.Runs, Is.EqualTo(expected.Runs));
            Assert.That(list.Boxes.Select(box => box.Bounds), Is.EqualTo(expected.Boxes.Select(box => box.Bounds)));
            for (int y = 0; y < 160; y += 4)
                for (int x = 0; x < 240; x += 4)
                    Assert.That(list.HitTest(x, y)?.Id, Is.EqualTo(expected.HitTest(x, y)?.Id), $"frame={frame} {x},{y}");
        }
    }

    [Test]
    public void BackgroundOnlyChangeKeepsPolygonAndHitBoxSnapshots()
    {
        var (doc, styles, flow, target) = Load();
        var list = flow.Layout(doc, 240, 160);
        var before = list.Boxes.Single(box => box.Element == target);
        target.Style.SetProperty("background-color", "rgba(90,70,40,.4)");
        Assert.That(styles.TryConsumeInlinePolygonPaintChange(target), Is.True);
        Assert.That(flow.TryPreparePolygonPaintUpdate(target, out var update), Is.True);
        Assert.That(flow.ApplyPolygonPaintUpdates(new[] { update }), Is.False);
        Assert.That(list.Boxes.Single(box => box.Element == target), Is.SameAs(before));
    }

    [Test]
    public void ParentTransformAndLeafPaintShareTheFullLayoutOracle()
    {
        var (doc, styles, flow, target) = Load();
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
        var list = flow.Layout(doc, 240, 160);
        var parent = (HTMLElement)doc.GetElementById("outer")!;
        var referenceParent = (HTMLElement)referenceDoc.GetElementById("outer")!;
        parent.Style.SetProperty("transform", "rotate(7deg)");
        referenceParent.Style.SetProperty("transform", "rotate(7deg)");
        list = flow.Layout(doc, 240, 160);
        parent.Style.SetProperty("transform", "translate(12px,3px) rotate(17deg)");
        referenceParent.Style.SetProperty("transform", "translate(12px,3px) rotate(17deg)");
        target.Style.SetProperty("clip-path", "polygon(0px 0px,60% 4px,80% 90%)");
        referenceTarget.Style.SetProperty("clip-path", "polygon(0px 0px,60% 4px,80% 90%)");
        Assert.That(styles.TryConsumeInlineTransformChange(parent), Is.True);
        Assert.That(flow.TryPrepareTransformUpdate(parent, out _, out var transform), Is.True);
        Assert.That(styles.TryConsumeInlinePolygonPaintChange(target), Is.True);
        Assert.That(flow.TryPreparePolygonPaintUpdate(target, out var update), Is.True);
        flow.ApplyTransformUpdate(parent, transform);
        flow.ApplyPolygonPaintUpdates(new[] { update });
        referenceStyles.Invalidate();
        var expected = referenceFlow.Layout(referenceDoc, 240, 160);
        Assert.That(list.Runs, Is.EqualTo(expected.Runs));
        for (int y = 0; y < 160; y += 4)
            for (int x = 0; x < 240; x += 4)
                Assert.That(list.HitTest(x, y)?.Id, Is.EqualTo(expected.HitTest(x, y)?.Id), $"{x},{y}");
    }

    [TestCase("#p{border:2px solid blue}", "")]
    [TestCase("#p{border-radius:8px}", "")]
    [TestCase("#p{outline:2px solid blue}", "")]
    [TestCase("", "<span>child</span>")]
    public void OtherDecorationsAndDescendantsRetainFullLayout(string css, string child)
    {
        var (doc, _, flow, target) = Load(css, child);
        flow.Layout(doc, 240, 160);
        Assert.That(flow.TryPreparePolygonPaintUpdate(target, out _), Is.False);
    }

    [TestCase("left", "20px")]
    [TestCase("clip-path", "polygon(0px 0px,2em 0px,40px 40px)")]
    [TestCase("background-color", "currentcolor")]
    [TestCase("background-color", "CanvasText")]
    [TestCase("background-color", "var(--tone)")]
    public void LayoutOrContextDependentDeltasCannotClaimPaintOnly(string property, string value)
    {
        var (doc, styles, flow, target) = Load("html{--tone:red}");
        flow.Layout(doc, 240, 160);
        target.Style.SetProperty(property, value);
        Assert.That(styles.TryConsumeInlinePolygonPaintChange(target), Is.False);
    }

    [Test]
    public void ZeroAlphaFillFallsBackRatherThanInventingAnAbsentPaintRun()
    {
        var (doc, styles, flow, target) = Load();
        flow.Layout(doc, 240, 160);
        target.Style.SetProperty("background-color", "rgba(0,0,0,0)");
        Assert.That(styles.TryConsumeInlinePolygonPaintChange(target), Is.True);
        Assert.That(flow.TryPreparePolygonPaintUpdate(target, out _), Is.False);
    }

    [TestCase("before")]
    [TestCase("after")]
    public void ObservedPseudoInheritanceRejectsPaintOnlyAndRefreshesItsValues(string pseudo)
    {
        var (doc, styles, flow, target) = Load("#p::" + pseudo + "{clip-path:inherit;background-color:inherit}");
        flow.Layout(doc, 240, 160);
        // As with the existing opacity contract, a pseudo's live computed view must
        // first be observed; the cascade does not eagerly materialize every pseudo.
        var observed = styles.GetComputedStyle(target, pseudo);
        _ = observed.GetPropertyValue("clip-path");
        target.Style.SetProperty("clip-path", "polygon(0px 0px,60% 4px,80% 90%)");
        target.Style.SetProperty("background-color", "rgba(90,70,40,.4)");
        Assert.That(styles.TryConsumeInlinePolygonPaintChange(target), Is.False);
        var parent = styles.GetComputedStyle(target);
        Assert.That(observed.GetPropertyValue("clip-path"), Is.EqualTo(parent.GetPropertyValue("clip-path")));
        Assert.That(observed.GetPropertyValue("background-color"), Is.EqualTo(parent.GetPropertyValue("background-color")));
    }
}
