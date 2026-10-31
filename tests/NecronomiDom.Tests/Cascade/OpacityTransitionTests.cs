using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

public sealed class OpacityTransitionTests
{
    private static HTMLElement Element()
    {
        var document = new Document().Implementation.CreateHTMLDocument("opacity-transition");
        var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element); return element;
    }
    private static CSSStyleDeclaration Style(HTMLElement element, string css)
    {
        element.Style.CssText = css;
        return element.OwnerDocument!.DefaultView.StyleEngine.GetComputedValues(element);
    }
    private static double Current(OpacityTransitionController controller, Element element)
    {
        Assert.That(controller.TryGetOpacity(element, out double opacity), Is.True); return opacity;
    }
    private static List<TransitionEvent> Events(Element element)
    {
        var events = new List<TransitionEvent>();
        foreach (string name in new[] { "transitionrun", "transitionstart", "transitionend", "transitioncancel" })
            element.AddEventListener(name, new DelegateEventListener(e => events.Add((TransitionEvent)e)));
        return events;
    }
    private const string Ease = "transition:opacity .25s ease-out;";

    [Test]
    public void FirstBindingDoesNotAnimateAndAuthoredDurationEasesToExactEndpoint()
    {
        var element = Element(); var events = Events(element); var controller = new OpacityTransitionController();
        controller.UpdateStyle(element, Style(element, Ease + "opacity:1"), 0);
        Assert.That(controller.Sample(.1), Is.False); controller.DispatchEvents(); Assert.That(events, Is.Empty);
        controller.UpdateStyle(element, Style(element, Ease + "opacity:.55"), .1);
        controller.Sample(.225);
        double current = Current(controller, element);
        Assert.That(current, Is.LessThan(.775).And.GreaterThan(.55));
        controller.Sample(.35); controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(.55)); Assert.That(controller.HasRunningTransitions, Is.False);
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events.TrueForAll(e => e.IsTrusted && e.Bubbles && e.Cancelable && e.PropertyName == "opacity"), Is.True);
        Assert.That(events[1].ElapsedTime, Is.Zero); Assert.That(events[2].ElapsedTime, Is.EqualTo(.25));
    }
    [Test]
    public void ReversalStartsAtCurrentValueAndShortensByOriginalEasedProgress()
    {
        var element = Element(); var events = Events(element); var controller = new OpacityTransitionController();
        controller.UpdateStyle(element, Style(element, Ease + "opacity:1"), 0);
        controller.UpdateStyle(element, Style(element, Ease + "opacity:0"), 0);
        controller.Sample(.125); double value = Current(controller, element);
        double shortened = .25 * (1 - value);
        controller.UpdateStyle(element, Style(element, Ease + "opacity:1"), .125);
        Assert.That(Current(controller, element), Is.EqualTo(value));
        controller.Sample(.125 + shortened); controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(1));
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitioncancel", "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events[2].ElapsedTime, Is.EqualTo(.125));
        Assert.That(events[5].ElapsedTime, Is.EqualTo(shortened).Within(1e-12));
    }
    [Test]
    public void PositiveDelayHasZeroStartElapsedEvenWhenFirstFrameIsLate()
    {
        var element = Element(); var events = Events(element); var controller = new OpacityTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:opacity .2s linear .1s;opacity:1"), 0);
        controller.UpdateStyle(element, Style(element, "transition:opacity .2s linear .1s;opacity:0"), 0);
        Assert.That(controller.Sample(.05), Is.False); controller.DispatchEvents(); Assert.That(events.Count, Is.EqualTo(1));
        controller.Sample(.15); controller.DispatchEvents();
        Assert.That(Current(controller, element), Is.EqualTo(.75).Within(1e-12)); Assert.That(events[1].ElapsedTime, Is.Zero);
    }
    [Test]
    public void NegativeDelayAndZeroDurationWithPositiveDelayProduceRealPhaseEvents()
    {
        var element = Element(); var events = Events(element); var controller = new OpacityTransitionController();
        controller.UpdateStyle(element, Style(element, "transition:opacity .2s linear -.1s;opacity:1"), 0);
        controller.UpdateStyle(element, Style(element, "transition:opacity .2s linear -.1s;opacity:0"), 0);
        Assert.That(Current(controller, element), Is.EqualTo(.5)); controller.DispatchEvents();
        Assert.That(events[0].ElapsedTime, Is.EqualTo(.1)); Assert.That(events[1].ElapsedTime, Is.EqualTo(.1));
        controller.Remove(element, .05); controller.DispatchEvents(); Assert.That(events[^1].ElapsedTime, Is.EqualTo(.15).Within(1e-12));
        events.Clear();
        controller.UpdateStyle(element, Style(element, "transition:opacity 0s linear .1s;opacity:1"), .1);
        controller.UpdateStyle(element, Style(element, "transition:opacity 0s linear .1s;opacity:0"), .1);
        controller.Sample(.2); controller.DispatchEvents();
        Assert.That(events.ConvertAll(e => e.Type), Is.EqualTo(new[] { "transitionrun", "transitionstart", "transitionend" }));
        Assert.That(events.TrueForAll(e => e.ElapsedTime == 0), Is.True);
    }
    [Test]
    public void RemovingTransitionCancelsAndRebindHasNoStaleIdentityOrAnimation()
    {
        var element = Element(); var events = Events(element); var controller = new OpacityTransitionController();
        controller.UpdateStyle(element, Style(element, Ease + "opacity:1"), 0);
        controller.UpdateStyle(element, Style(element, Ease + "opacity:.55"), 0);
        controller.UpdateStyle(element, Style(element, "transition:none;opacity:.55"), .1);
        controller.DispatchEvents(); Assert.That(events[^1].Type, Is.EqualTo("transitioncancel"));
        Assert.That(Current(controller, element), Is.EqualTo(.55));
        controller.Remove(element, .1); Assert.That(controller.TryGetOpacity(element, out _), Is.False);
        controller.UpdateStyle(element, Style(element, Ease + "opacity:0"), .1);
        Assert.That(Current(controller, element), Is.Zero); Assert.That(controller.HasRunningTransitions, Is.False);
        controller.Clear(.2); Assert.That(controller.Count, Is.Zero);
    }
    [Test]
    public void LastMatchingPropertyAndModuloTimingListsAreUsedAndUnsupportedTimingFailsExplicitly()
    {
        var element = Element();
        var style = OpacityTransitionStyle.Resolve(Style(element, "opacity:75%;transition-property:opacity,color,all;transition-duration:100ms,200ms;transition-delay:-50ms;transition-timing-function:linear,ease-out"));
        Assert.That(style.Opacity, Is.EqualTo(.75)); Assert.That(style.Duration, Is.EqualTo(.1)); Assert.That(style.Delay, Is.EqualTo(-.05));
        Assert.That(style.Easing.Sample(.5), Is.EqualTo(.5));
        Assert.Throws<NotSupportedException>(() => OpacityTransitionStyle.Resolve(Style(element, "transition:opacity .25s steps(2);opacity:1")));
    }
    [Test]
    public void ClockCannotGoBackwardAndSteadySampleDoesNotAllocate()
    {
        var element = Element(); var controller = new OpacityTransitionController();
        controller.UpdateStyle(element, Style(element, Ease + "opacity:1"), 1);
        controller.Sample(1);
        // The clock is monotonic and cannot replay a window; the sample above is the warm-up.
        Assert.That(AllocationWindow.Measure(100, i => controller.Sample(1 + i * .01), warmupWindows: 0), Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Sample(0));
    }
}
