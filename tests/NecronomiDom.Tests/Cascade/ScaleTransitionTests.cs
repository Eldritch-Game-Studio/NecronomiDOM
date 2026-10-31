using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

public sealed class ScaleTransitionTests
{
    private static HTMLElement Element()
    {
        var document = new Document().Implementation.CreateHTMLDocument("scale-transition");
        var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element); return element;
    }
    private static CSSStyleDeclaration Style(HTMLElement element, string css)
    { element.Style.CssText = css; return element.OwnerDocument!.DefaultView.StyleEngine.GetComputedValues(element); }
    private static AnimatedScale Current(ScaleTransitionController controller, Element element)
    { Assert.That(controller.TryGetScale(element, out var scale), Is.True); return scale; }
    private static List<TransitionEvent> Events(Element element)
    {
        var events = new List<TransitionEvent>();
        foreach (string type in new[] { "transitionrun", "transitionstart", "transitionend", "transitioncancel" })
            element.AddEventListener(type, new DelegateEventListener(e => events.Add((TransitionEvent)e)));
        return events;
    }
    private const string Focused = "transition:scale .12s ease-out;scale:1.06 1.06;";
    [TestCase(.03, .4375f)]
    [TestCase(.06, .75f)]
    [TestCase(.09, .9375f)]
    public void SimplifiedFocusedScaleUsesFloatLerpAndPolynomialEaseOut(double clock, float eased)
    {
        var element = Element(); var controller = new ScaleTransitionController(GeometryTransitionProfile.Simplified);
        var before = ScaleTransitionStyle.Resolve(Style(element, "scale:none"));
        controller.UpdateStyle(element, Style(element, Focused), 0, before);
        controller.Sample(clock);
        float expected = 1f + (1.06f - 1f) * eased;
        Assert.That(Current(controller, element), Is.EqualTo(new AnimatedScale(expected, expected)));
        controller.Sample(.12); Assert.That(Current(controller, element), Is.EqualTo(new AnimatedScale(1.06f, 1.06f)));
    }
    [Test]
    public void OrdinaryCssProfileKeepsCubicEasingAndDoubleTargets()
    {
        var element = Element(); var controller = new ScaleTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:scale .12s ease-out;scale:1"), 0);
        controller.UpdateStyle(element, Style(element, Focused), 0);
        controller.Sample(.06);
        double expected = 1 + .06 * new CssCubicEasing(0, 0, .58, 1).Sample(.5);
        Assert.That(Current(controller, element).X, Is.EqualTo(expected).Within(1e-15));
        Assert.That(Current(controller, element).X, Is.Not.EqualTo(1.045));
        Assert.That(controller.Profile, Is.EqualTo(GeometryTransitionProfile.Css));
    }
    [Test]
    public void AuthoredFocusRemovalCancelsAndSnapsInsteadOfInventingALeaveTransition()
    {
        var element = Element(); var controller = new ScaleTransitionController(GeometryTransitionProfile.Simplified); var events = Events(element);
        var before = ScaleTransitionStyle.Resolve(Style(element, "scale:none"));
        controller.UpdateStyle(element, Style(element, Focused), 0, before); controller.Sample(.06);
        controller.UpdateStyle(element, Style(element, "scale:none;transition:background-color 120ms ease-out"), .06);
        controller.DispatchEvents();
        Assert.That(controller.TryGetScale(element, out _), Is.False);
        Assert.That(controller.HasRunningTransitions, Is.False);
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitioncancel" }));
        Assert.That(events[^1].ElapsedTime, Is.EqualTo(.06));
    }
    [Test]
    public void IdentityOverrideDoesNotGrowOrEmitAFalseTransition()
    {
        var element = Element(); var controller = new ScaleTransitionController(GeometryTransitionProfile.Simplified); var events = Events(element);
        var before = ScaleTransitionStyle.Resolve(Style(element, "scale:none"));
        controller.UpdateStyle(element, Style(element, "transition:scale .12s ease-out;scale:1 1"), 0, before);
        controller.Sample(.06); controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(AnimatedScale.Identity));
        Assert.That(events, Is.Empty); Assert.That(controller.HasRunningTransitions, Is.False);
    }
    [Test]
    public void ReversalAndLiveRetargetAreContinuousWithExactShortenedDuration()
    {
        var element = Element(); var controller = new ScaleTransitionController(GeometryTransitionProfile.Simplified); var events = Events(element);
        const string timing = "transition:scale .12s ease-out;";
        controller.UpdateStyle(element, Style(element, timing + "scale:1"), 0);
        controller.UpdateStyle(element, Style(element, timing + "scale:2 3"), 0);
        controller.Sample(.06); var before = Current(controller, element);
        controller.UpdateStyle(element, Style(element, timing + "scale:1"), .06);
        Assert.That(Current(controller, element), Is.EqualTo(before));
        controller.Sample(.15); controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(AnimatedScale.Identity));
        Assert.That(events[^1].ElapsedTime, Is.EqualTo(.09));
        Assert.That(events.TrueForAll(e => e.IsTrusted && e.Bubbles && e.Cancelable && e.PropertyName == "scale"), Is.True);
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear;scale:3 5"), .15);
        controller.Sample(.65); before = Current(controller, element);
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear;scale:4 6"), .65);
        Assert.That(Current(controller, element), Is.EqualTo(before));
        controller.Sample(1.15); Assert.That(Current(controller, element), Is.EqualTo(new AnimatedScale(3, 4.5)));
    }
    [Test]
    public void DelaysAndTimingOnlyMutationRetainOriginalClockAndTypedLists()
    {
        var element = Element(); var controller = new ScaleTransitionController(); var events = Events(element);
        var typed = ScaleTransitionStyle.Resolve(Style(element, "scale:2 3;transition-property:scale,width,all;transition-duration:120ms,250ms;transition-delay:30ms;transition-timing-function:linear,ease"));
        Assert.That(typed.Duration, Is.EqualTo(.12)); Assert.That(typed.Delay, Is.EqualTo(.03)); Assert.That(typed.Easing, Is.EqualTo(CssCubicEasing.Linear));
        const string timing = "transition:scale 1s linear .1s;";
        controller.UpdateStyle(element, Style(element, timing + "scale:1"), 0);
        controller.UpdateStyle(element, Style(element, timing + "scale:3"), 0);
        Assert.That(controller.Sample(.05), Is.False); controller.DispatchEvents(); Assert.That(events.Count, Is.EqualTo(1));
        controller.UpdateStyle(element, Style(element, "transition:scale 8s ease-in;scale:3"), .3);
        controller.Sample(.6); Assert.That(Current(controller, element), Is.EqualTo(new AnimatedScale(2, 2)));
        controller.Clear(.6);
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear -.25s;scale:1"), .6);
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear -.25s;scale:3"), .6);
        Assert.That(Current(controller, element), Is.EqualTo(new AnimatedScale(1.5, 1.5)));
    }
    [Test]
    public void ExplicitIdentityAndActiveNoneEndpointRetainContainingContextUntilCompletion()
    {
        var element = Element(); var controller = new ScaleTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear;scale:2"), 0);
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear;scale:none"), 0);
        controller.Sample(.5); Assert.That(Current(controller, element), Is.EqualTo(new AnimatedScale(1.5, 1.5)));
        Assert.That(controller.Sample(1), Is.True); Assert.That(controller.TryGetScale(element, out _), Is.False);
        Assert.That(controller.Contains(element), Is.True);
        controller.UpdateStyle(element, Style(element, "transition:none;scale:1"), 1);
        Assert.That(Current(controller, element), Is.EqualTo(AnimatedScale.Identity));
    }
    [Test]
    public void WithdrawalReentryAndReentrantDispatchClearOwnedState()
    {
        var element = Element(); var controller = new ScaleTransitionController(); var events = Events(element);
        element.AddEventListener("transitionrun", new DelegateEventListener(_ => controller.DispatchEvents()));
        controller.UpdateStyle(element, Style(element, "transition:scale 1s;scale:1"), 0);
        controller.UpdateStyle(element, Style(element, "transition:scale 1s;scale:2"), 0);
        controller.Remove(element, .25); controller.DispatchEvents();
        Assert.That(events[^1].Type, Is.EqualTo("transitioncancel")); Assert.That(controller.Count, Is.Zero);
        controller.UpdateStyle(element, Style(element, Focused), .25);
        Assert.That(controller.HasRunningTransitions, Is.False); controller.Clear(.25); Assert.That(controller.Count, Is.Zero);
    }
    [Test]
    public void SamplingAllocatesNothingAndRejectsNonMonotonicClock()
    {
        var element = Element(); var controller = new ScaleTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear;scale:1"), 0);
        controller.UpdateStyle(element, Style(element, "transition:scale 1s linear;scale:2"), 0);
        controller.Sample(.1); controller.DispatchEvents();
        // The interior clock is monotonic and cannot replay a window; the sample above is the warm-up.
        Assert.That(AllocationWindow.Measure(50, i => controller.Sample(.1 + (i + 1) * .001), warmupWindows: 0), Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Sample(0));
    }
    [Test]
    public void SimplifiedScaleEqualityRetainsVectorThresholdNotCssExactEquality()
    {
        var element = Element(); var simplified = new ScaleTransitionController(GeometryTransitionProfile.Simplified);
        // Near one, existing CSS declaration equality compares six-significant-digit serialization
        // and treats 1.000001 as unchanged. The same absolute delta at zero reaches both consumers.
        simplified.UpdateStyle(element, Style(element, "transition:scale 1s;scale:0"), 0);
        var changed = Style(element, "transition:scale 1s;scale:.000001");
        Assert.That(ScaleTransitionStyle.Resolve(changed).Scale, Is.EqualTo(new AnimatedScale(.000001, .000001)));
        simplified.UpdateStyle(element, changed, 0);
        Assert.That(simplified.HasRunningTransitions, Is.False);
        var css = new ScaleTransitionController();
        css.UpdateStyle(element, Style(element, "transition:scale 1s;scale:0"), 0);
        css.UpdateStyle(element, Style(element, "transition:scale 1s;scale:.000001"), 0);
        Assert.That(css.HasRunningTransitions, Is.True);
    }
}
