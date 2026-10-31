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
public sealed class FlowOpacityIntegrationTests
{
    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }
    private static (Document Doc, StyleEngine Styles, OpacityTransitionController Controller, FlowLayout Flow) Fixture(string body, string css)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;border:0 solid black;" +
            "font-family:monospace;font-size:16px;line-height:20px;appearance:none}html,body{width:100%;height:100%}" + css + "</style>" + body,
            "opacity", WebUrl.Parse("res:///opacity.html")!).Document;
        var styles = new StyleEngine(doc); var controller = new OpacityTransitionController();
        return (doc, styles, controller, new FlowLayout(styles, new Metrics(), "monospace", null, controller));
    }
    [Test]
    public void InitialOpaqueCandidateHasActualCarrierAndReferenceIdentityOwner()
    {
        var (doc, styles, controller, flow) = Fixture("<div id=target></div>", "#target{width:40px;height:40px;transition:opacity 1s linear}");
        var target = doc.GetElementById("target")!;
        controller.UpdateStyle(target, styles.GetComputedStyle(target), 0);
        var list = flow.Layout(doc, 200, 100);
        var begin = list.Runs.OfType<BeginOpacityRun>().Single();
        Assert.That(begin.Opacity, Is.EqualTo(1));
        Assert.That(flow.TryGetOpacityOwner(begin, out var owner), Is.True);
        Assert.That(owner, Is.SameAs(target));
        Assert.That(flow.TryGetOpacityOwner(new BeginOpacityRun(begin.Bounds, begin.Opacity), out _), Is.False);
        Assert.That(controller.HasRunningTransitions, Is.False);
    }
    [Test]
    public void ActualClassMutationSamplesPaintWithoutReplacingComputedTarget()
    {
        var (doc, styles, controller, flow) = Fixture("<div id=target></div>",
            "#target{width:40px;height:40px;transition:opacity 1s linear}.dim{opacity:0}");
        var target = doc.GetElementById("target")!;
        controller.UpdateStyle(target, styles.GetComputedStyle(target), 0);
        target.ClassList.Add("dim"); controller.UpdateStyle(target, styles.GetComputedStyle(target), 0);
        controller.Sample(.25);
        var begin = flow.Layout(doc, 200, 100).Runs.OfType<BeginOpacityRun>().Single();
        Assert.That(begin.Opacity, Is.EqualTo(.75f));
        Assert.That(styles.GetComputedStyle(target).GetPropertyValue("opacity"), Is.EqualTo("0"));
        Assert.That(flow.TryGetOpacityOwner(begin, out var owner), Is.True);
        Assert.That(owner, Is.SameAs(target));
    }
    [Test]
    public void StaticAndTranslatedGroupsRetainTheirReferenceIdentityOwner()
    {
        var (doc, styles, controller, flow) = Fixture("<div id=static></div><div id=anchor><div id=target></div></div>",
            "#static{width:20px;height:20px;opacity:.5}#anchor{position:absolute;bottom:10px;width:100px}" +
            "#target{height:20px;transition:opacity 1s linear}");
        var target = doc.GetElementById("target")!;
        controller.UpdateStyle(target, styles.GetComputedStyle(target), 0);
        var groups = flow.Layout(doc, 200, 100).Runs.OfType<BeginOpacityRun>().ToArray();
        Assert.That(groups.Length, Is.EqualTo(2));
        Assert.That(flow.TryGetOpacityOwner(groups[0], out var staticOwner), Is.True);
        Assert.That(staticOwner, Is.SameAs(doc.GetElementById("static")));
        Assert.That(groups[1].Bounds.Y, Is.EqualTo(70));
        Assert.That(flow.TryGetOpacityOwner(groups[1], out var owner), Is.True);
        Assert.That(owner, Is.SameAs(target));
    }
    [Test]
    public void ObservedBeforeChangeStartsNewEligibilityButOrdinaryFirstBindingStaysInitial()
    {
        var (doc, styles, controller, _) = Fixture("<div id=target></div>",
            ".dim{opacity:0;transition:opacity 1s linear}");
        var target = doc.GetElementById("target")!;
        double before = OpacityTransitionStyle.Resolve(styles.GetComputedStyle(target)).Opacity;
        target.ClassList.Add("dim");
        controller.UpdateStyle(target, styles.GetComputedStyle(target), 0, before);
        Assert.That(controller.HasRunningTransitions, Is.True);
        controller.Sample(.25); Assert.That(controller.TryGetOpacity(target, out double opacity), Is.True);
        Assert.That(opacity, Is.EqualTo(.75));
        var initial = new OpacityTransitionController();
        initial.UpdateStyle(target, styles.GetComputedStyle(target), 0);
        Assert.That(initial.HasRunningTransitions, Is.False);
        Assert.That(initial.TryGetOpacity(target, out opacity), Is.True);
        Assert.That(opacity, Is.EqualTo(0));
    }
    [Test]
    public void NoProviderDoesNotSilentlyAcceptNoninitialRuntimeTransition()
    {
        var (doc, styles, _, _) = Fixture("<div id=target></div>", "#target{transition:opacity 1s linear}");
        Assert.Throws<NotSupportedException>(() => new FlowLayout(styles, new Metrics(), "monospace").Layout(doc, 200, 100));
    }
}
