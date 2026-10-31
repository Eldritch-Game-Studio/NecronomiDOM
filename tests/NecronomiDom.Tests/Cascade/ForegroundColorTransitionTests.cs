using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

public sealed class ForegroundColorTransitionTests
{
    private static HTMLElement Element()
    {
        var document = new Document().Implementation.CreateHTMLDocument("foreground-transition");
        var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element); return element;
    }
    private static CSSStyleDeclaration Style(HTMLElement element, string css)
    { element.Style.CssText = css; return element.OwnerDocument!.DefaultView.StyleEngine.GetComputedValues(element); }
    private static Rgba Current(ForegroundColorTransitionController controller, Element element)
    { Assert.That(controller.TryGetForegroundColor(element, out var color), Is.True); return color; }
    private static List<TransitionEvent> Events(Element element)
    {
        var events = new List<TransitionEvent>();
        foreach (string type in new[] { "transitionrun", "transitionstart", "transitionend", "transitioncancel" })
            element.AddEventListener(type, new DelegateEventListener(e => events.Add((TransitionEvent)e)));
        return events;
    }
    private const string Authored = "transition-property:color,background-color;transition-duration:.12s,.12s;transition-timing-function:ease-out,ease-out;";

    [TestCase(.03, .3781381308)]
    [TestCase(.06, .6846432657)]
    [TestCase(.09, .9065353490)]
    public void CssProfileRetainsExistingCubicEaseOut(double time, double expected)
    {
        var element = Element(); var controller = new ForegroundColorTransitionController();
        controller.UpdateStyle(element, Style(element, Authored + "color:black"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "color:white"), 0);
        controller.Sample(time);
        Assert.That(Current(controller, element).R, Is.EqualTo(expected).Within(1e-6));
        Assert.That(controller.Profile, Is.EqualTo(ForegroundColorTransitionProfile.Css));
    }
    [TestCase(.03, .4375)]
    [TestCase(.06, .75)]
    [TestCase(.09, .9375)]
    public void SimplifiedProfileUsesPolynomialEaseOutAndStraightChannels(double time, double progress)
    {
        var element = Element(); var controller = new ForegroundColorTransitionController(ForegroundColorTransitionProfile.Simplified);
        controller.UpdateStyle(element, Style(element, Authored + "color:rgb(240,232,210)"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "color:rgb(214,170,88)"), 0);
        controller.Sample(time); var actual = Current(controller, element);
        Assert.That(actual.R, Is.EqualTo((240 - 26 * progress) / 255).Within(1e-7));
        Assert.That(actual.G, Is.EqualTo((232 - 62 * progress) / 255).Within(1e-7));
        Assert.That(actual.B, Is.EqualTo((210 - 122 * progress) / 255).Within(1e-7));
        Assert.That(actual.A, Is.EqualTo(1));
    }
    [TestCase(ForegroundColorTransitionProfile.Css, 0, 1)]
    [TestCase(ForegroundColorTransitionProfile.Simplified, .5, .5)]
    public void TransparentColorInterpolationKeepsCssPremultiplicationDistinctFromSimplifiedStraightRgba(
        ForegroundColorTransitionProfile profile, double red, double blue)
    {
        var element = Element(); var controller = new ForegroundColorTransitionController(profile);
        controller.UpdateStyle(element, Style(element, "transition:color .12s linear;color:rgba(255,0,0,0)"), 0);
        controller.UpdateStyle(element, Style(element, "transition:color .12s linear;color:blue"), 0);
        controller.Sample(.06); var actual = Current(controller, element);
        Assert.That(actual, Is.EqualTo(new Rgba((float)red, 0, (float)blue, .5f)));
    }
    [Test]
    public void FirstBindingDoesNotAnimateAndCompletedOverrideRetiresWithExactTrustedPropertyEvents()
    {
        var element = Element(); var controller = new ForegroundColorTransitionController(); var events = Events(element);
        controller.UpdateStyle(element, Style(element, Authored + "color:red"), 0);
        Assert.That(controller.TryGetForegroundColor(element, out _), Is.False);
        controller.DispatchEvents(); Assert.That(events, Is.Empty);
        controller.UpdateStyle(element, Style(element, Authored + "color:blue"), 0);
        Assert.That(Current(controller, element), Is.EqualTo(new Rgba(1, 0, 0, 1)));
        Assert.That(controller.Sample(.12), Is.True);
        Assert.That(controller.TryGetForegroundColor(element, out _), Is.False, "Settled state must not mask inherited ink");
        Assert.That(controller.HasRunningTransitions, Is.False);
        controller.DispatchEvents();
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events.TrueForAll(e => e.IsTrusted && e.Bubbles && e.Cancelable && e.PropertyName == "color"), Is.True);
        Assert.That(events[^1].ElapsedTime, Is.EqualTo(.12));
        Assert.That(ForegroundColorTransitionStyle.Resolve(Style(element, Authored + "color:blue")).Color, Is.EqualTo(new Rgba(0, 0, 1, 1)));
    }
    [Test]
    public void TypedListsSelectTheLastColorOrAllWithExistingModuloAndDelayRules()
    {
        var style = ForegroundColorTransitionStyle.Resolve(Style(Element(), "color:red;transition-property:color,background-color,all;" +
            "transition-duration:120ms,240ms;transition-delay:-30ms;transition-timing-function:linear,ease"));
        Assert.That(style.Enabled, Is.True); Assert.That(style.Duration, Is.EqualTo(.12));
        Assert.That(style.Delay, Is.EqualTo(-.03)); Assert.That(style.Easing, Is.EqualTo(CssCubicEasing.Linear));
        var unrelated = ForegroundColorTransitionStyle.Resolve(Style(Element(), "color:red;transition:background-color .12s"));
        Assert.That(unrelated.Enabled, Is.False);
    }
    [Test]
    public void PositiveAndNegativeDelayKeepPendingInkAndElapsedEventSemantics()
    {
        var element = Element(); var controller = new ForegroundColorTransitionController(); var events = Events(element);
        const string delayed = "transition:color .12s linear .03s;";
        controller.UpdateStyle(element, Style(element, delayed + "color:black"), 0);
        controller.UpdateStyle(element, Style(element, delayed + "color:white"), 0);
        Assert.That(controller.Sample(.02), Is.False); Assert.That(Current(controller, element).R, Is.Zero);
        controller.DispatchEvents(); Assert.That(events.Count, Is.EqualTo(1));
        controller.Sample(.09); Assert.That(Current(controller, element).R, Is.EqualTo(.5f));
        controller.DispatchEvents(); Assert.That(events[1].ElapsedTime, Is.Zero);
        controller.Clear(.09);
        controller.UpdateStyle(element, Style(element, "transition:color .12s linear -.03s;color:black"), .1);
        controller.UpdateStyle(element, Style(element, "transition:color .12s linear -.03s;color:white"), .1);
        Assert.That(Current(controller, element).R, Is.EqualTo(.25f));
    }
    [Test]
    public void InterruptedReversalIsContinuousAndUsesExistingEasedShortening()
    {
        var element = Element(); var controller = new ForegroundColorTransitionController(); var events = Events(element);
        const string css = "transition:color .12s ease;";
        controller.UpdateStyle(element, Style(element, css + "color:black"), 0);
        controller.UpdateStyle(element, Style(element, css + "color:white"), 0);
        controller.Sample(.06); Rgba before = Current(controller, element);
        controller.UpdateStyle(element, Style(element, css + "color:black"), .06);
        Assert.That(Current(controller, element), Is.EqualTo(before));
        controller.Sample(.1562884066); controller.DispatchEvents();
        Assert.That(controller.TryGetForegroundColor(element, out _), Is.False);
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitioncancel", "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events[2].ElapsedTime, Is.EqualTo(.06)); Assert.That(events[^1].ElapsedTime, Is.EqualTo(.0962884065).Within(1e-9));
    }
    [Test]
    public void RetargetStartsFromActualInkAndTimingOnlyChangesDoNotRestart()
    {
        var element = Element(); var controller = new ForegroundColorTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:color 1s linear;color:black"), 0);
        controller.UpdateStyle(element, Style(element, "transition:color 1s linear;color:red"), 0);
        controller.Sample(.25);
        controller.UpdateStyle(element, Style(element, "transition:color 9s ease-in;color:red"), .25);
        controller.Sample(.5); Assert.That(Current(controller, element).R, Is.EqualTo(.5f));
        controller.UpdateStyle(element, Style(element, "transition:color 1s linear;color:blue"), .5);
        Assert.That(Current(controller, element), Is.EqualTo(new Rgba(.5f, 0, 0, 1)));
        controller.Sample(1); Assert.That(Current(controller, element), Is.EqualTo(new Rgba(.25f, 0, .5f, 1)));
    }
    [Test]
    public void DisableRemoveClearAndReentrantDispatchReleaseAllActiveOverrides()
    {
        var element = Element(); var controller = new ForegroundColorTransitionController(); var events = Events(element);
        element.AddEventListener("transitionrun", new DelegateEventListener(_ => controller.DispatchEvents()));
        controller.UpdateStyle(element, Style(element, Authored + "color:black"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "color:white"), 0);
        controller.Remove(element, .03); controller.DispatchEvents();
        Assert.That(events[^1].Type, Is.EqualTo("transitioncancel")); Assert.That(controller.Contains(element), Is.False);
        controller.UpdateStyle(element, Style(element, Authored + "color:white"), .03);
        Assert.That(controller.HasRunningTransitions, Is.False);
        controller.UpdateStyle(element, Style(element, Authored + "color:black"), .03);
        controller.UpdateStyle(element, Style(element, "transition:none;color:red"), .04);
        Assert.That(controller.TryGetForegroundColor(element, out _), Is.False);
        controller.Clear(.04); Assert.That(controller.Count, Is.Zero);
    }
    [Test]
    public void ObservedBeforeChangeAndMonotonicEventFreeSamplesAllocateNothing()
    {
        var element = Element(); var controller = new ForegroundColorTransitionController();
        controller.UpdateStyle(element, Style(element, Authored + "color:white"), 0, new Rgba(0, 0, 0, 1));
        controller.Sample(.03); Assert.That(Current(controller, element).R, Is.GreaterThan(0).And.LessThan(1));
        // The interior clock is monotonic and cannot replay a window; the sample above is the warm-up.
        Assert.That(AllocationWindow.Measure(50, i => controller.Sample(.03 + (i + 1) * .001), warmupWindows: 0), Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Sample(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Sample(double.NaN));
    }
}
