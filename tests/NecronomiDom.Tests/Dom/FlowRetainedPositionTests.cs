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
public sealed class FlowRetainedPositionTests
{
    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }

    private static (Document Doc, StyleEngine Styles, FlowLayout Flow, HTMLElement Target) Load(string css = "", string inline = "left:20px;top:20px;right:auto;bottom:auto")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html,body,div,span{display:block;margin:0;padding:0;" +
            "font-family:monospace;font-size:16px;line-height:20px;appearance:none}html,body{width:100%;height:100%}" +
            "#outer{position:absolute;left:20px;top:10px;width:200px;height:140px;overflow:hidden;transform:rotate(7deg)}" +
            "#p{position:absolute;width:70px;height:50px;background:rgba(30,70,90,.8);transform:rotate(9deg);overflow:hidden}" +
            "#content{display:flex;width:60px;height:20px;background:blue}" +
            "#c{position:absolute;width:10px;height:8px;background:red}" +
            "#other{position:absolute;left:2px;top:2px;width:12px;height:12px;background:green}" + css +
            "</style><div id=outer><div id=p style='" + inline + "'><div id=content><span>fish</span></div>" +
            "<div id=c style='left:8px;top:30px'></div></div><div id=other></div></div>",
            "retained-position", WebUrl.Parse("res:///retained-position.html")!).Document;
        var styles = new StyleEngine(doc);
        return (doc, styles, new FlowLayout(styles, new Metrics(), "monospace"), (HTMLElement)doc.GetElementById("p")!);
    }

    private static void SameDisplay(DisplayList actual, DisplayList expected)
    {
        Assert.That(actual.Runs.Count, Is.EqualTo(expected.Runs.Count));
        for (int i = 0; i < actual.Runs.Count; i++)
        {
            if (actual.Runs[i] is BeginTransformRun a && expected.Runs[i] is BeginTransformRun b)
            {
                Assert.That(a.Bounds, Is.EqualTo(b.Bounds));
                // Conjugating an existing matrix and resolving it at the new origin are
                // algebraically identical; their floating-point operation order differs.
                Assert.That(new[] { a.Transform.A, a.Transform.B, a.Transform.C, a.Transform.D, a.Transform.E, a.Transform.F },
                    Is.EqualTo(new[] { b.Transform.A, b.Transform.B, b.Transform.C, b.Transform.D, b.Transform.E, b.Transform.F }).Within(1e-10));
            }
            else Assert.That(actual.Runs[i], Is.EqualTo(expected.Runs[i]), "run " + i);
        }
        Assert.That(actual.Boxes.Select(box => box.Bounds), Is.EqualTo(expected.Boxes.Select(box => box.Bounds)));
        Assert.That(actual.Boxes.Select(box => box.ScrollOverflow), Is.EqualTo(expected.Boxes.Select(box => box.ScrollOverflow)));
        for (int y = 0; y < 160; y += 4)
            for (int x = 0; x < 240; x += 4)
                Assert.That(actual.HitTest(x + .23, y + .37)?.Id, Is.EqualTo(expected.HitTest(x + .23, y + .37)?.Id), $"hit {x},{y}");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedInteriorMovementMatchesFreshPaintBoxesOverflowAndHits(bool percentages)
    {
        var (doc, styles, flow, target) = Load();
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
        var list = flow.Layout(doc, 240, 160);
        var unrelated = list.Boxes.Single(box => box.Element.Id == "other");
        for (int frame = 0; frame < 20; frame++)
        {
            string left = percentages ? (11 + frame) + "%" : (22 + frame * 2) + "px";
            target.Style.SetProperty("left", left); referenceTarget.Style.SetProperty("left", left);
            Assert.That(styles.TryConsumeInlinePositionChange(target), Is.True);
            Assert.That(styles.TryConsumeInlinePositionChange(target), Is.False);
            Assert.That(flow.TryPrepareAbsolutePositionUpdate(target, out var update), Is.True);
            Assert.That(flow.ApplyAbsolutePositionUpdates(new[] { update }), Is.True);
            Assert.That(list.Boxes.Single(box => box.Element.Id == "other"), Is.SameAs(unrelated));
            referenceStyles.Invalidate();
            SameDisplay(list, referenceFlow.Layout(referenceDoc, 240, 160));
            Assert.That(doc.GetElementById("outer")!.ScrollState!.Width,
                Is.EqualTo(referenceDoc.GetElementById("outer")!.ScrollState!.Width));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedPositionBatchesPreserveContainingBlocksInEitherOrder(bool childFirst)
    {
        var (doc, styles, flow, target) = Load();
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load();
        var child = (HTMLElement)doc.GetElementById("c")!;
        var referenceChild = (HTMLElement)referenceDoc.GetElementById("c")!;
        var list = flow.Layout(doc, 240, 160);
        for (int frame = 0; frame < 10; frame++)
        {
            string parentX = (22 + frame * 2) + "px", childX = (9 + frame) + "px";
            target.Style.SetProperty("left", parentX); referenceTarget.Style.SetProperty("left", parentX);
            child.Style.SetProperty("left", childX); referenceChild.Style.SetProperty("left", childX);
            Assert.That(styles.TryConsumeInlinePositionChange(target), Is.True);
            Assert.That(styles.TryConsumeInlinePositionChange(child), Is.True);
            Assert.That(flow.TryPrepareAbsolutePositionUpdate(target, out var parentMove), Is.True);
            Assert.That(flow.TryPrepareAbsolutePositionUpdate(child, out var childMove), Is.True);
            flow.ApplyAbsolutePositionUpdates(childFirst ? new[] { childMove, parentMove } : new[] { parentMove, childMove });
            referenceStyles.Invalidate();
            SameDisplay(list, referenceFlow.Layout(referenceDoc, 240, 160));
        }
    }

    [TestCase("right", "left:auto;right:20px;top:20px;bottom:auto", "30px")]
    [TestCase("top", "left:20px;right:auto;top:20px;bottom:auto", "35px")]
    [TestCase("bottom", "left:20px;right:auto;top:auto;bottom:20px", "30px")]
    public void CanonicalOppositeAndVerticalInsetsRemainEquivalent(string property, string initial, string next)
    {
        var (doc, styles, flow, target) = Load(inline: initial);
        var (referenceDoc, referenceStyles, referenceFlow, referenceTarget) = Load(inline: initial);
        var list = flow.Layout(doc, 240, 160);
        target.Style.SetProperty(property, next); referenceTarget.Style.SetProperty(property, next);
        Assert.That(styles.TryConsumeInlinePositionChange(target), Is.True);
        Assert.That(flow.TryPrepareAbsolutePositionUpdate(target, out var update), Is.True);
        flow.ApplyAbsolutePositionUpdates(new[] { update }); referenceStyles.Invalidate();
        SameDisplay(list, referenceFlow.Layout(referenceDoc, 240, 160));
    }

    [TestCase("#p{width:auto}")]
    [TestCase("#p{position:relative}")]
    [TestCase("#p{overflow:auto}")]
    [TestCase("#p{overflow:scroll}")]
    public void UnsupportedSizingFlowOrScrollbarTopologyRetainsFullLayout(string css)
    {
        var (doc, styles, flow, target) = Load(css);
        flow.Layout(doc, 240, 160); target.Style.SetProperty("left", "25px");
        Assert.That(styles.TryConsumeInlinePositionChange(target), Is.True);
        Assert.That(flow.TryPrepareAbsolutePositionUpdate(target, out _), Is.False);
    }

    [Test]
    public void ChangedStretchHeightRetainsFullDescendantPercentageResolution()
    {
        var (doc, styles, flow, target) = Load("#p{height:auto}", "left:20px;top:20px;right:auto;bottom:20px");
        flow.Layout(doc, 240, 160); target.Style.SetProperty("bottom", "25px");
        Assert.That(styles.TryConsumeInlinePositionChange(target), Is.True);
        Assert.That(flow.TryPrepareAbsolutePositionUpdate(target, out _), Is.False);
    }

    [Test]
    public void OverflowExtentChangeRequiresCanonicalLayoutInsteadOfStaleScrollMetrics()
    {
        var (doc, styles, flow, target) = Load();
        flow.Layout(doc, 240, 160); double before = doc.GetElementById("outer")!.ScrollState!.Width;
        target.Style.SetProperty("left", "190px");
        Assert.That(styles.TryConsumeInlinePositionChange(target), Is.True);
        Assert.That(flow.TryPrepareAbsolutePositionUpdate(target, out _), Is.False);
        flow.Layout(doc, 240, 160);
        Assert.That(doc.GetElementById("outer")!.ScrollState!.Width, Is.GreaterThan(before));
    }

    [Test]
    public void PreparedMovementCannotCrossAFullLayoutGeneration()
    {
        var (doc, styles, flow, target) = Load();
        flow.Layout(doc, 240, 160); target.Style.SetProperty("left", "25px");
        Assert.That(styles.TryConsumeInlinePositionChange(target), Is.True);
        Assert.That(flow.TryPrepareAbsolutePositionUpdate(target, out var update), Is.True);
        flow.Layout(doc, 240, 160);
        Assert.Throws<ArgumentException>(() => flow.ApplyAbsolutePositionUpdates(new[] { update }));
    }

    [TestCase("[style]{width:71px}")]
    [TestCase("#p{left:20px!important}")]
    [TestCase("#c{left:inherit!important}")]
    [TestCase("#p::before{left:inherit}")]
    public void ObservableCascadeAndInheritedPositionDependenciesKeepFullResolution(string css)
    {
        var (doc, styles, flow, target) = Load(css);
        flow.Layout(doc, 240, 160);
        styles.GetComputedStyle(target, "before");
        target.Style.SetProperty("left", "25px");
        Assert.That(styles.TryConsumeInlinePositionChange(target), Is.False);
    }

    [Test]
    public void FullStyleInvalidationRetiresPendingPositionDelta()
    {
        var (doc, styles, flow, target) = Load();
        flow.Layout(doc, 240, 160);
        target.Style.SetProperty("left", "25px");
        styles.Invalidate();
        Assert.That(styles.TryConsumeInlinePositionChange(target), Is.False);
    }
}
