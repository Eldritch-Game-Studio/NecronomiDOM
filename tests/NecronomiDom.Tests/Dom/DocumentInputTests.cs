using System;
using System.Collections.Generic;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class DocumentInputTests
{
    private static (Document Doc, DocumentInput Input, StyleEngine Styles) Fixture(string markup)
    {
        var doc = HtmlImporter.Import("<!doctype html><body>" + markup, "focus", WebUrl.Parse("res:///focus.html")!).Document;
        var display = new DisplayList(); int y = 0;
        foreach (Element element in doc.QuerySelectorAll("*"))
            display.Boxes.Add(new LayoutBox(element, new Rect(0, y++ * 40, 160, 40), true));
        var styles = new StyleEngine(doc);
        var input = new DocumentInput(doc, styles); input.UpdateGeometry(display, 640, 1000);
        return (doc, input, styles);
    }
    private static KeyboardEvent Key(string type, string key, bool shift = false, bool repeat = false) => new(type,
        new KeyboardEventInit { Bubbles = true, Cancelable = true, Composed = true, Key = key, Code = key == " " ? "Space" : key, ShiftKey = shift, Repeat = repeat });
    [Test]
    public void PositiveTabOrderAutofocusNegativeDisabledAndInert()
    {
        var (doc, input, styles) = Fixture("<button id=z autofocus>Z</button><button id=b tabindex=2>B</button><button id=a tabindex=1>A</button><button disabled>D</button><div inert><button>I</button></div><a id=n href='#' tabindex=-1>N</a><a id=l href='#'>L</a>");
        using (input)
        {
            Assert.That(doc.FocusedElement!.Id, Is.EqualTo("z"));
            input.MoveSequential(false); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("l"));
            Assert.That(styles.GetComputedStyle(doc.FocusedElement!).GetPropertyValue("outline-offset"), Is.EqualTo("1px"));
            input.MoveSequential(false); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("a"));
            input.MoveSequential(false); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("b"));
            input.MoveSequential(true); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("a"));
            ((HTMLElement)doc.GetElementById("n")!).Focus(); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("n"));
            input.MoveSequential(false); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("l"));
        }
        Assert.That(doc.FocusedElement, Is.Null);
    }
    [Test]
    public void FocusEventsRelatedTargetsCaptureBubblingAndRemovalFixup()
    {
        var (doc, input, _) = Fixture("<button id=a>A</button><button id=b>B</button>");
        using (input)
        {
            var log = new List<string>();
            foreach (string type in new[] { "focus", "focusin", "blur", "focusout" })
                doc.AddEventListener(type, new DelegateEventListener(e => log.Add($"{e.Type}:{((Element)e.Target!).Id}:{(((FocusEvent)e).RelatedTarget is Element r ? r.Id : "")}:{e.Bubbles}:{e.Cancelable}:{e.IsTrusted}")), true);
            var a = (HTMLElement)doc.GetElementById("a")!; var b = (HTMLElement)doc.GetElementById("b")!;
            a.Focus(); log.Clear(); b.Focus();
            Assert.That(log, Is.EqualTo(new[] { "blur:a:b:False:False:True", "focusout:a:b:True:False:True", "focus:b:a:False:False:True", "focusin:b:a:True:False:True" }));
            Assert.That(b.Matches(":focus-visible"), Is.True); Assert.That(doc.Body!.Matches(":focus-within"), Is.True);
            log.Clear(); b.Focus(); Assert.That(log, Is.Empty, "Same-focus calls must not redispatch focus events.");
            b.Focus(new FocusOptions { FocusVisible = false });
            Assert.That(b.Matches(":focus-visible"), Is.True); Assert.That(b.Matches(":focus"), Is.True);
            Assert.That(log, Is.Empty, "Same-target options must not update the focus ring or redispatch focus events.");
            log.Clear(); b.Remove(); Assert.That(log, Is.Empty); Assert.That(doc.ActiveElement, Is.SameAs(doc.Body));
            Assert.That(b.PseudoState & ElementPseudoState.Focus, Is.EqualTo(ElementPseudoState.None));
            a.Focus(new FocusOptions { FocusVisible = false, PreventScroll = true }); Assert.That(a.Matches(":focus-visible"), Is.False);
            a.Focus(new FocusOptions { FocusVisible = true }); Assert.That(a.Matches(":focus-visible"), Is.False);
            a.SetAttribute("disabled", ""); Assert.That(doc.FocusedElement, Is.Null);
            a.RemoveAttribute("disabled"); a.Focus(); doc.Body.Inert = true; Assert.That(doc.FocusedElement, Is.Null);
        }
    }
    [Test]
    public void ExplicitRingOptionsDoNotOverwritePointerModality()
    {
        var (doc, input, _) = Fixture("<button id=a>A</button><button id=b>B</button>");
        using (input)
        {
            var a = (HTMLElement)doc.GetElementById("a")!; var b = (HTMLElement)doc.GetElementById("b")!;
            var display = new DisplayList();
            display.Boxes.Add(new LayoutBox(a, new Rect(0, 0, 160, 40), true));
            display.Boxes.Add(new LayoutBox(b, new Rect(0, 40, 160, 40), true));
            input.UpdateGeometry(display, 640, 480);
            input.PointerButton(8, 8, true); input.PointerButton(8, 8, false);
            Assert.That(a.Matches(":focus-visible"), Is.False);
            b.Focus(new FocusOptions { FocusVisible = true }); Assert.That(b.Matches(":focus-visible"), Is.True);
            b.Blur(); a.Focus(); Assert.That(a.Matches(":focus-visible"), Is.False);
        }
    }
    [Test]
    public void KeyboardDefaultsCancellationRepeatAndReleaseClearing()
    {
        var (doc, input, _) = Fixture("<button id=a>A</button><a id=l href='#'>L</a>");
        using (input)
        {
            var a = (HTMLElement)doc.GetElementById("a")!; a.Focus(); int clicks = 0;
            a.AddEventListener("click", new DelegateEventListener(_ => clicks++));
            input.Key(Key("keydown", " ")); input.Key(Key("keydown", " ", repeat: true)); Assert.That(clicks, Is.Zero);
            input.Key(Key("keyup", " ")); Assert.That(clicks, Is.EqualTo(1));
            input.Key(Key("keydown", "Enter")); Assert.That(a.Matches(":active"), Is.True);
            input.Key(Key("keyup", "Enter")); Assert.That(clicks, Is.EqualTo(2)); Assert.That(a.Matches(":active"), Is.False);
            a.AddEventListener("keydown", new DelegateEventListener(e => e.PreventDefault()));
            input.Key(Key("keydown", "Enter")); Assert.That(clicks, Is.EqualTo(2));
            input.Key(Key("keydown", " ")); input.Key(Key("keyup", " ")); Assert.That(clicks, Is.EqualTo(2));
            a.AddEventListener("click", new DelegateEventListener(e => e.PreventDefault())); Assert.That(input.Submit(), Is.False);
            input.ReleaseInput(); input.Key(Key("keyup", " ")); Assert.That(clicks, Is.EqualTo(3));
            Assert.That(a.PseudoState & ElementPseudoState.Active, Is.EqualTo(ElementPseudoState.None));
        }
    }
    [Test]
    public void PointerMouseDownCancellationHoverActiveAndClickCommonAncestor()
    {
        var (doc, input, _) = Fixture("<button id=a><span id=x>X</span><span id=y>Y</span></button><button id=b>B</button>");
        using (input)
        {
            // Use actual child geometry for the two release targets; click bubbles from their button ancestor.
            var display = new DisplayList(); var a = (HTMLElement)doc.GetElementById("a")!;
            display.Boxes.Add(new LayoutBox(a, new Rect(0, 0, 200, 40), true));
            display.Boxes.Add(new LayoutBox(doc.GetElementById("x")!, new Rect(0, 0, 100, 40), true));
            display.Boxes.Add(new LayoutBox(doc.GetElementById("y")!, new Rect(100, 0, 100, 40), true));
            input.UpdateGeometry(display, 640, 480);
            input.PointerMove(20, 20); Assert.That(a.Matches(":hover"), Is.True);
            a.AddEventListener("mousedown", new DelegateEventListener(e => e.PreventDefault()));
            input.PointerButton(20, 20, true); Assert.That(a.Matches(":active"), Is.True); Assert.That(doc.FocusedElement, Is.Null);
            EventTarget? target = null; a.AddEventListener("click", new DelegateEventListener(e => target = e.Target));
            input.PointerButton(120, 20, false); Assert.That(target, Is.SameAs(a)); Assert.That(a.Matches(":active"), Is.False);
            input.PointerButton(20, 20, true); input.ReleaseInput(); input.PointerButton(20, 20, false);
            Assert.That(input.PointerActivationCount, Is.EqualTo(1)); Assert.That(a.Matches(":hover"), Is.False);
        }
    }
    [Test]
    public void SpatialNavigationUsesBoxesContainerAndCancelableNavigationEvent()
    {
        var (doc, input, _) = Fixture("<style>#c{spatial-navigation-contain:contain;spatial-navigation-function:grid}</style><div id=c><button id=a>A</button><button id=b>B</button></div><button id=d>D</button>");
        using (input)
        {
            var a = (HTMLElement)doc.GetElementById("a")!; a.Focus();
            Assert.That(input.Navigate("down"), Is.True); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("b"));
            Assert.That(input.Navigate("down"), Is.True); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("d"));
            doc.GetElementById("d")!.AddEventListener("navbeforefocus", new DelegateEventListener(e => e.PreventDefault()));
            Assert.That(input.Navigate("up"), Is.False); Assert.That(doc.FocusedElement!.Id, Is.EqualTo("d"));
        }
    }
}
