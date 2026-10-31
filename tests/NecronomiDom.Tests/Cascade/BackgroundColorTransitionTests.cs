using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

public sealed class BackgroundColorTransitionTests
{
    private static HTMLElement Element()
    {
        var doc = new Document().Implementation.CreateHTMLDocument("background-transition");
        var element = (HTMLElement)doc.CreateElement("div"); doc.Body!.AppendChild(element); return element;
    }
    private static CSSStyleDeclaration Style(HTMLElement element, string css)
    { element.Style.CssText = css; return element.OwnerDocument!.DefaultView.StyleEngine.GetComputedValues(element); }
    private static Rgba Current(BackgroundColorTransitionController controller, Element element)
    { Assert.That(controller.TryGetBackgroundColor(element, out var color), Is.True); return color; }
    private static List<TransitionEvent> Events(Element element)
    {
        var events = new List<TransitionEvent>();
        foreach (string type in new[] { "transitionrun", "transitionstart", "transitionend", "transitioncancel" })
            element.AddEventListener(type, new DelegateEventListener(e => events.Add((TransitionEvent)e)));
        return events;
    }
    private const string Ease = "transition:background-color .12s;";

    [TestCase(.03, .408510591)]
    [TestCase(.06, .802403388)]
    [TestCase(.09, .960458978)]
    public void CssDefaultEaseRetainsBrowserCubicTrajectory(double clock, double expected)
    {
        var element = Element(); var controller = new BackgroundColorTransitionController();
        controller.UpdateStyle(element, Style(element, Ease + "background-color:black"), 0);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:white"), 0);
        controller.Sample(clock);
        Assert.That(Current(controller, element).R, Is.EqualTo(expected).Within(1e-6));
    }
    [TestCase(.03, .409375)]
    [TestCase(.06, .725)]
    [TestCase(.09, .928125)]
    public void SimplifiedProfileUsesPolynomialNamedEaseAndStraightChannels(double clock, double expected)
    {
        var element = Element();
        var simplified = new BackgroundColorTransitionController(BackgroundColorTransitionProfile.Simplified);
        simplified.UpdateStyle(element, Style(element, Ease + "background-color:rgba(0,0,0,.45)"), 0);
        simplified.UpdateStyle(element, Style(element, Ease + "background-color:rgba(72,56,32,.92)"), 0);
        simplified.Sample(clock);
        var color = Current(simplified, element);
        Assert.That(color.R, Is.EqualTo(72 / 255d * expected).Within(1e-7));
        Assert.That(color.G, Is.EqualTo(56 / 255d * expected).Within(1e-7));
        Assert.That(color.B, Is.EqualTo(32 / 255d * expected).Within(1e-7));
        Assert.That(color.A, Is.EqualTo(.45 + .47 * expected).Within(1e-7));
        Assert.That(simplified.Profile, Is.EqualTo(BackgroundColorTransitionProfile.Simplified));
    }

    [Test]
    public void CssInterpolatesPremultipliedSrgbNotSimplifiedStraightAlpha()
    {
        var element = Element(); var controller = new BackgroundColorTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:background-color .12s linear;background-color:rgba(255,0,0,0)"), 0);
        controller.UpdateStyle(element, Style(element, "transition:background-color .12s linear;background-color:blue"), 0);
        controller.Sample(.06);
        Assert.That(Current(controller, element), Is.EqualTo(new Rgba(0, 0, 1, .5f)));
    }
    [Test]
    public void FirstBindingEndpointsAndTrustedPropertyEventsAreExact()
    {
        var element = Element(); var controller = new BackgroundColorTransitionController(); var events = Events(element);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:rgba(0,0,0,.45)"), 0);
        controller.Sample(.1); controller.DispatchEvents(); Assert.That(events, Is.Empty);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:rgba(72,56,32,.92)"), .1);
        controller.Sample(.22); controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(new Rgba(72 / 255f, 56 / 255f, 32 / 255f, .92f)));
        Assert.That(controller.HasRunningTransitions, Is.False);
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events.TrueForAll(e => e.IsTrusted && e.Bubbles && e.Cancelable && e.PropertyName == "background-color"), Is.True);
        Assert.That(events[^1].ElapsedTime, Is.EqualTo(.12));
    }
    [Test]
    public void EnterLeaveReversalIsContinuousAndUsesEasedShortening()
    {
        var element = Element(); var controller = new BackgroundColorTransitionController(); var events = Events(element);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:black"), 0);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:white"), 0);
        controller.Sample(.06); Rgba before = Current(controller, element);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:black"), .06);
        Assert.That(Current(controller, element), Is.EqualTo(before));
        controller.Sample(.06 + .12 * new CssCubicEasing(.25, .1, .25, 1).Sample(.5) + 1e-12);
        controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(new Rgba(0, 0, 0, 1)));
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitioncancel", "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events[2].ElapsedTime, Is.EqualTo(.06));
        Assert.That(events[^1].ElapsedTime, Is.EqualTo(.0962884065).Within(1e-9));
    }
    [Test]
    public void LiveTargetRetargetsFromPaintButTimingOnlyMutationDoesNotRestart()
    {
        var element = Element(); var controller = new BackgroundColorTransitionController(); var events = Events(element);
        controller.UpdateStyle(element, Style(element, "transition:background-color 1s linear;background-color:black"), 0);
        controller.UpdateStyle(element, Style(element, "transition:background-color 1s linear;background-color:red"), 0);
        controller.Sample(.25);
        controller.UpdateStyle(element, Style(element, "transition:background-color 9s ease-in;background-color:red"), .25);
        controller.Sample(.5); Assert.That(Current(controller, element).R, Is.EqualTo(.5f));
        controller.UpdateStyle(element, Style(element, "transition:background-color 1s linear;background-color:blue"), .5);
        Assert.That(Current(controller, element), Is.EqualTo(new Rgba(.5f, 0, 0, 1)));
        controller.Sample(1); Assert.That(Current(controller, element), Is.EqualTo(new Rgba(.25f, 0, .5f, 1)));
        controller.DispatchEvents(); Assert.That(events.FindAll(e => e.Type == "transitioncancel").Count, Is.EqualTo(1));
    }
    [Test]
    public void DelaysAndLastPropertyModuloListsUseExistingTypedCascade()
    {
        var element = Element();
        var style = BackgroundColorTransitionStyle.Resolve(Style(element, "background-color:red;transition-property:background-color,opacity,all;transition-duration:120ms,240ms;transition-delay:-30ms;transition-timing-function:linear,ease"));
        Assert.That(style.Duration, Is.EqualTo(.12)); Assert.That(style.Delay, Is.EqualTo(-.03)); Assert.That(style.Easing, Is.EqualTo(CssCubicEasing.Linear));
        var controller = new BackgroundColorTransitionController(); var events = Events(element);
        const string css = "transition:background-color .12s linear .03s;";
        controller.UpdateStyle(element, Style(element, css + "background-color:black"), 0);
        controller.UpdateStyle(element, Style(element, css + "background-color:white"), 0);
        Assert.That(controller.Sample(.02), Is.False);
        controller.DispatchEvents(); Assert.That(events.Count, Is.EqualTo(1));
        controller.Sample(.09); Assert.That(Current(controller, element).R, Is.EqualTo(.5f));
        controller.DispatchEvents(); Assert.That(events[1].ElapsedTime, Is.Zero);
        controller.Clear(.09);
        const string negative = "transition:background-color .12s linear -.03s;";
        controller.UpdateStyle(element, Style(element, negative + "background-color:black"), .1);
        controller.UpdateStyle(element, Style(element, negative + "background-color:white"), .1);
        Assert.That(Current(controller, element).R, Is.EqualTo(.25f));
    }
    [Test]
    public void RemovalDisableReentryAndReentrantDispatchDoNotLeakState()
    {
        var element = Element(); var controller = new BackgroundColorTransitionController(); var events = Events(element);
        element.AddEventListener("transitionrun", new DelegateEventListener(_ => controller.DispatchEvents()));
        controller.UpdateStyle(element, Style(element, Ease + "background-color:black"), 0);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:white"), 0);
        controller.Remove(element, .03); controller.DispatchEvents();
        Assert.That(events[^1].Type, Is.EqualTo("transitioncancel"));
        Assert.That(controller.TryGetBackgroundColor(element, out _), Is.False);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:white"), .03);
        Assert.That(controller.HasRunningTransitions, Is.False);
        controller.UpdateStyle(element, Style(element, Ease + "background-color:black"), .03);
        controller.UpdateStyle(element, Style(element, "transition:none;background-color:red"), .04);
        Assert.That(Current(controller, element), Is.EqualTo(new Rgba(1, 0, 0, 1)));
        controller.Clear(.04); Assert.That(controller.Count, Is.Zero);
    }
    [Test]
    public void RealObservedBeforeChangeStartsEligibilityAndSteadySamplingDoesNotAllocate()
    {
        var element = Element(); var controller = new BackgroundColorTransitionController();
        controller.UpdateStyle(element, Style(element, Ease + "background-color:white"), 0, new Rgba(0, 0, 0, 1));
        controller.Sample(.03); Assert.That(Current(controller, element).R, Is.GreaterThan(0).And.LessThan(1));
        // The interior clock is monotonic and cannot replay a window; the sample above is the warm-up.
        Assert.That(AllocationWindow.Measure(50, i => controller.Sample(.03 + (i + 1) * .001), warmupWindows: 0), Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Sample(0));
    }
}
