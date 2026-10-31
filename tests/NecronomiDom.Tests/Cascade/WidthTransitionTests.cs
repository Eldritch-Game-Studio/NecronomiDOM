using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

public sealed class WidthTransitionTests
{
    private static HTMLElement Element()
    {
        var document = new Document().Implementation.CreateHTMLDocument("width-transition");
        var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element); return element;
    }
    private static CSSStyleDeclaration Style(HTMLElement element, string css)
    { element.Style.CssText = css; return element.OwnerDocument!.DefaultView.StyleEngine.GetComputedValues(element); }
    private static AnimatedWidth Current(WidthTransitionController controller, Element element)
    { Assert.That(controller.TryGetWidth(element, out var width), Is.True); return width; }
    private static List<TransitionEvent> Events(Element element)
    {
        var events = new List<TransitionEvent>();
        foreach (string type in new[] { "transitionrun", "transitionstart", "transitionend", "transitioncancel" })
            element.AddEventListener(type, new DelegateEventListener(e => events.Add((TransitionEvent)e)));
        return events;
    }
    private const string Authored = "transition:width .25s ease-out;";

    [TestCase(.0625, 43.75)]
    [TestCase(.125, 75)]
    [TestCase(.1875, 93.75)]
    public void SimplifiedPercentageHasExactFloatQuadraticInterior(double now, double percent)
    {
        var element = Element(); var simplified = new WidthTransitionController(GeometryTransitionProfile.Simplified);
        simplified.UpdateStyle(element, Style(element, Authored + "width:0%"), 0);
        simplified.UpdateStyle(element, Style(element, Authored + "width:100%"), 0);
        Assert.That(simplified.Sample(now), Is.True);
        Assert.That(Current(simplified, element), Is.EqualTo(new AnimatedWidth(0, percent, true)));
        Assert.That(Current(simplified, element).Resolve(240), Is.EqualTo(percent * 2.4).Within(1e-12));
        Assert.That(element.Style.GetPropertyValue("width"), Is.EqualTo("100%"));
    }
    [Test]
    public void DefaultCssRetainsCubicAndIndependentCurrentPercentageBasis()
    {
        var element = Element(); var controller = new WidthTransitionController();
        controller.UpdateStyle(element, Style(element, Authored + "width:0%"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "width:100%"), 0);
        controller.Sample(.125);
        double expected = new CssCubicEasing(0, 0, .58, 1).Sample(.5) * 100;
        Assert.That(Current(controller, element).Percent, Is.EqualTo(expected).Within(1e-12));
        Assert.That(Current(controller, element).Percent, Is.Not.EqualTo(75));
        Assert.That(Current(controller, element).Resolve(400), Is.EqualTo(expected * 4).Within(1e-12));
        Assert.That(controller.Profile, Is.EqualTo(GeometryTransitionProfile.Css));
    }
    [TestCase("40px", "80%", 200, 50, true)]
    [TestCase("20%", "120px", 200, 80, false)]
    public void SimplifiedConvertsStartToEndUnitUsingPreviousParentWidth(string from, string to, double basis, double midpoint, bool percent)
    {
        var element = Element(); var controller = new WidthTransitionController(GeometryTransitionProfile.Simplified);
        const string timing = "transition:width 1s linear;";
        controller.UpdateStyle(element, Style(element, timing + "width:" + from), 0);
        controller.UpdateStyle(element, Style(element, timing + "width:" + to), 0, basis);
        controller.Sample(.5); var current = Current(controller, element);
        Assert.That(percent ? current.Percent : current.Pixels, Is.EqualTo(midpoint));
        Assert.That(current.HasPercentage, Is.EqualTo(percent));
        Assert.That(current.Resolve(300), Is.EqualTo(percent ? midpoint * 3 : midpoint));
    }
    [Test]
    public void CssMixedUnitsRemainAPairRatherThanFreezingAConversionBasis()
    {
        var element = Element(); var controller = new WidthTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:40px"), 0);
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:80%"), 0, 999);
        controller.Sample(.5); var current = Current(controller, element);
        Assert.That(current, Is.EqualTo(new AnimatedWidth(20, 40, true)));
        Assert.That(current.Resolve(200), Is.EqualTo(100));
        Assert.That(current.Resolve(300), Is.EqualTo(140));
    }
    [TestCase(null)]
    [TestCase(0d)]
    [TestCase(-1d)]
    public void SimplifiedConversionWithoutPositivePercentBasisSnapsWithoutInventingPixels(double? parent)
    {
        var element = Element(); var controller = new WidthTransitionController(GeometryTransitionProfile.Simplified); var events = Events(element);
        controller.UpdateStyle(element, Style(element, Authored + "width:40px"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "width:80%"), 0, parent);
        controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(new AnimatedWidth(0, 80, true)));
        Assert.That(events, Is.Empty); Assert.That(controller.HasRunningTransitions, Is.False);
    }
    [Test]
    public void SimplifiedApproximatelyZeroPercentCanConvertToPixelsWithoutAParent()
    {
        var element = Element(); var controller = new WidthTransitionController(GeometryTransitionProfile.Simplified);
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:0%"), 0);
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:80px"), 0);
        controller.Sample(.5); Assert.That(Current(controller, element), Is.EqualTo(new AnimatedWidth(40, 0)));
    }
    [Test]
    public void ReversalShortensByEasedProgressAndDispatchesTrustedPropertyEvents()
    {
        var element = Element(); var controller = new WidthTransitionController(GeometryTransitionProfile.Simplified); var events = Events(element);
        controller.UpdateStyle(element, Style(element, Authored + "width:0%"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "width:100%"), 0);
        controller.Sample(.125); var before = Current(controller, element);
        controller.UpdateStyle(element, Style(element, Authored + "width:0%"), .125);
        Assert.That(Current(controller, element), Is.EqualTo(before));
        controller.Sample(.3125); controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(new AnimatedWidth(0, 0, true)));
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitioncancel", "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events[^1].ElapsedTime, Is.EqualTo(.1875));
        Assert.That(events.TrueForAll(e => e.IsTrusted && e.Bubbles && e.Cancelable && e.PropertyName == "width"), Is.True);
    }
    [Test]
    public void LiveRetargetStartsAtSampleAndTimingOnlyMutationDoesNotRestart()
    {
        var element = Element(); var controller = new WidthTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:0px"), 0);
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:100px"), 0);
        controller.UpdateStyle(element, Style(element, "transition:width 8s ease-in;width:100px"), .25);
        controller.Sample(.5); Assert.That(Current(controller, element).Pixels, Is.EqualTo(50));
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:150px"), .5);
        Assert.That(Current(controller, element).Pixels, Is.EqualTo(50));
        controller.Sample(1); Assert.That(Current(controller, element).Pixels, Is.EqualTo(100));
    }
    [Test]
    public void DelaysModuloPropertyListsAndBeforeChangeUseExistingTypedCascade()
    {
        var element = Element();
        var style = WidthTransitionStyle.Resolve(Style(element, "width:70%;transition-property:width,scale,all;transition-duration:250ms,120ms;transition-delay:30ms;transition-timing-function:linear,ease"));
        Assert.That(style.Duration, Is.EqualTo(.25)); Assert.That(style.Delay, Is.EqualTo(.03)); Assert.That(style.Easing, Is.EqualTo(CssCubicEasing.Linear));
        var controller = new WidthTransitionController(); var events = Events(element);
        var before = WidthTransitionStyle.Resolve(Style(element, "width:10%"));
        controller.UpdateStyle(element, Style(element, "transition:width .25s linear .05s;width:90%"), 0, beforeChangeStyle: before);
        Assert.That(controller.Sample(.025), Is.False); controller.DispatchEvents(); Assert.That(events.Count, Is.EqualTo(1));
        controller.Sample(.175); Assert.That(Current(controller, element).Percent, Is.EqualTo(50).Within(1e-12));
        controller.Clear(.175);
        controller.UpdateStyle(element, Style(element, "transition:width .25s linear -.05s;width:0%"), .2);
        controller.UpdateStyle(element, Style(element, "transition:width .25s linear -.05s;width:100%"), .2);
        Assert.That(Current(controller, element).Percent, Is.EqualTo(20).Within(1e-12));
    }
    [Test]
    public void AutoRemovalWithdrawalReentryAndReentrantDispatchDoNotRetainAnimation()
    {
        var element = Element(); var controller = new WidthTransitionController(); var events = Events(element);
        element.AddEventListener("transitionrun", new DelegateEventListener(_ => controller.DispatchEvents()));
        controller.UpdateStyle(element, Style(element, Authored + "width:0%"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "width:100%"), 0);
        controller.UpdateStyle(element, Style(element, Authored + "width:auto"), .03);
        controller.DispatchEvents(); Assert.That(events[^1].Type, Is.EqualTo("transitioncancel"));
        Assert.That(controller.TryGetWidth(element, out _), Is.False);
        controller.UpdateStyle(element, Style(element, Authored + "width:100%"), .03);
        Assert.That(controller.HasRunningTransitions, Is.False);
        controller.UpdateStyle(element, Style(element, Authored + "width:0%"), .03);
        controller.UpdateStyle(element, Style(element, "transition:none;width:20%"), .04);
        Assert.That(Current(controller, element).Percent, Is.EqualTo(20));
        controller.Remove(element, .04); Assert.That(controller.Count, Is.Zero);
        controller.UpdateStyle(element, Style(element, Authored + "width:70%"), .05);
        Assert.That(controller.HasRunningTransitions, Is.False); controller.Clear(.05); Assert.That(controller.Count, Is.Zero);
    }
    [Test]
    public void InteriorSamplingDoesNotAllocateAndClockCannotRunBackward()
    {
        var element = Element(); var controller = new WidthTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:0px"), 0);
        controller.UpdateStyle(element, Style(element, "transition:width 1s linear;width:100px"), 0);
        controller.Sample(.1); controller.DispatchEvents();
        // The interior clock is monotonic and cannot replay a window; the sample above is the warm-up.
        Assert.That(AllocationWindow.Measure(50, i => controller.Sample(.1 + (i + 1) * .001), warmupWindows: 0), Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Sample(0));
    }
}
