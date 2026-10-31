using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class PointerCaptureTests
{
    private sealed class MonospaceMetrics : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * fontSize * 0.6, fontSize * 0.8, fontSize * 0.2);
    }
    private static (Document Doc, DocumentInput Input) Fixture()
    {
        var doc = HtmlImporter.Import("""
            <!doctype html><style>
            html,body,button{margin:0;padding:0;font-family:monospace;font-size:16px;line-height:20px;appearance:none}
            body{height:480px}
            button{display:block;box-sizing:border-box;width:80px;height:40px;border:1px solid #999;background:#ddd}
            </style><button id=a>A</button><button id=b>B</button><button id=c>C</button>
            """, "capture", WebUrl.Parse("res:///capture.html")!).Document;
        var styles = new StyleEngine(doc);
        var input = new DocumentInput(doc, styles);
        input.UpdateGeometry(new FlowLayout(styles, new MonospaceMetrics(), "monospace").Layout(doc, 640, 480), 640, 480);
        return (doc, input);
    }
    private static string Target(EventTarget? target) => target is Element e ? (e.Id.Length > 0 ? e.Id : e.NodeName) : target is Document ? "#document" : "";
    [Test]
    public void GotCaptureRemovalKeepsCurrentDetachedEventBeforeRestoringPhysicalBoundary()
    {
        var (doc, input) = Fixture(); using var owner = input;
        var a = doc.GetElementById("a")!; var b = doc.GetElementById("b")!;
        var order = new List<(string Type, string Target, double X)>();
        doc.AddEventListener("pointermove", new DelegateEventListener(e =>
            order.Add((e.Type, Target(e.Target), ((MouseEvent)e).ClientX))), true);
        doc.AddEventListener("pointerover", new DelegateEventListener(e =>
        {
            if (e.Target == a && ((MouseEvent)e).ClientX == 35) order.Add((e.Type, "a", 35));
        }), true);
        a.AddEventListener("pointerdown", new DelegateEventListener(e => b.SetPointerCapture(((PointerEvent)e).PointerId)));
        b.AddEventListener("gotpointercapture", new DelegateEventListener(_ => b.Remove()));
        b.AddEventListener("pointermove", new DelegateEventListener(e =>
        {
            Assert.That(b.IsConnected, Is.False);
            order.Add((e.Type, "b-detached", ((MouseEvent)e).ClientX));
        }));
        input.PointerMove(30, 20); input.PointerButton(30, 20, true);
        input.PointerMove(35, 20, 1); input.PointerMove(40, 20, 1); input.PointerButton(40, 20, false);
        Assert.That(order, Is.EqualTo(new[] { ("pointermove", "a", 30d), ("pointermove", "b-detached", 35d),
            ("pointerover", "a", 35d), ("pointermove", "a", 40d) }),
            "Chrome trusted mouse trace: the detached target receives x=35 before the physical boundary is restored; only x=40 moves on a.");
    }
    [Test]
    public void CaptureValidatesActiveIdsAndConnectionAndHasReflectsPendingState()
    {
        var (doc, input) = Fixture(); using var owner = input;
        var a = doc.GetElementById("a")!; var b = doc.GetElementById("b")!;
        Assert.That(a.HasPointerCapture(-1), Is.False);
        Assert.That(Assert.Throws<DomException>(() => a.SetPointerCapture(1))!.Name, Is.EqualTo("NotFoundError"));
        a.DispatchEvent(new PointerEvent("pointerdown", new PointerEventInit { PointerId = 1 }));
        Assert.That(Assert.Throws<DomException>(() => a.SetPointerCapture(1))!.Name, Is.EqualTo("NotFoundError"), "dispatchEvent must not create an active pointer.");
        input.PointerMove(10, 10); a.SetPointerCapture(1); Assert.That(a.HasPointerCapture(1), Is.False, "Hovering pointer is active but has no buttons.");
        input.PointerButton(10, 10, true);
        a.SetPointerCapture(1); Assert.That(a.HasPointerCapture(1), Is.True);
        b.SetPointerCapture(1); Assert.That(a.HasPointerCapture(1), Is.False); Assert.That(b.HasPointerCapture(1), Is.True);
        a.ReleasePointerCapture(1); Assert.That(b.HasPointerCapture(1), Is.True, "Only the pending owner can release.");
        b.ReleasePointerCapture(1); Assert.That(b.HasPointerCapture(1), Is.False);
        var detached = doc.CreateElement("div");
        Assert.That(Assert.Throws<DomException>(() => detached.SetPointerCapture(1))!.Name, Is.EqualTo("InvalidStateError"));
        Assert.That(Assert.Throws<DomException>(() => detached.SetPointerCapture(999))!.Name, Is.EqualTo("NotFoundError"), "Active-id validation precedes connection validation.");
        Assert.That(Assert.Throws<DomException>(() => a.ReleasePointerCapture(999))!.Name, Is.EqualTo("NotFoundError"));
        input.PointerButton(10, 10, false); a.SetPointerCapture(1); Assert.That(a.HasPointerCapture(1), Is.False);
        input.Dispose(); Assert.That(a.HasPointerCapture(1), Is.False);
        Assert.That(Assert.Throws<DomException>(() => a.ReleasePointerCapture(1))!.Name, Is.EqualTo("NotFoundError"));
        var synthetic = new PointerEvent("click"); Assert.That(synthetic.PointerId, Is.Zero); Assert.That(synthetic.PointerType, Is.Empty); Assert.That(synthetic.IsPrimary, Is.False);
    }
    [Test]
    public void OutsideGeometryCaptureAndCancellationClearAllStateWithoutReplayedActivation()
    {
        var (doc, input) = Fixture(); using var owner = input;
        var a = doc.GetElementById("a")!; var b = doc.GetElementById("b")!;
        a.AddEventListener("pointerdown", new DelegateEventListener(e => b.SetPointerCapture(((PointerEvent)e).PointerId)));
        var log = new List<string>(); int clicks = 0;
        foreach (string type in new[] { "gotpointercapture", "pointercancel", "pointerout", "pointerleave", "lostpointercapture" })
            b.AddEventListener(type, new DelegateEventListener(e => { log.Add(e.Type); if (e.Type == "pointercancel") { Assert.That(e.Cancelable, Is.False); Assert.That(e.IsTrusted && e.Bubbles && e.Composed, Is.True); } }));
        b.AddEventListener("click", new DelegateEventListener(_ => clicks++));
        input.PointerMove(20, 20); input.PointerButton(20, 20, true); input.PointerMove(800, 600, 1);
        Assert.That(b.Matches(":hover"), Is.True); Assert.That(a.Matches(":active"), Is.True);
        input.PointerCancel(); input.PointerButton(800, 600, false);
        Assert.That(log, Is.EqualTo(new[] { "gotpointercapture", "pointercancel", "pointerout", "pointerleave", "lostpointercapture" }));
        Assert.That(clicks, Is.Zero); Assert.That(input.HasPointerCaptureOverride, Is.False);
        Assert.That(a.Matches(":active") || b.Matches(":hover"), Is.False);
        input.PointerMove(20, 20); input.PointerButton(20, 20, true); input.PointerMove(800, 600, 1); input.PointerButton(800, 600, false);
        Assert.That(clicks, Is.EqualTo(1), "Captured up outside the document still clicks its captured target once.");
    }
    [Test]
    public void CapturedUpRetainsItsClickTargetWhenUpListenerDetachesIt()
    {
        var (doc, input) = Fixture(); using var owner = input;
        var a = doc.GetElementById("a")!; var b = doc.GetElementById("b")!;
        a.AddEventListener("pointerdown", new DelegateEventListener(e => b.SetPointerCapture(((PointerEvent)e).PointerId)));
        b.AddEventListener("pointerup", new DelegateEventListener(_ => b.Remove()));
        var targetLog = new List<string>(); int documentClicks = 0;
        foreach (string type in new[] { "mouseup", "click" })
            b.AddEventListener(type, new DelegateEventListener(e =>
            {
                targetLog.Add(e.Type);
                if (e is PointerEvent click) Assert.That(click.IsPrimary, Is.False);
            }));
        doc.AddEventListener("click", new DelegateEventListener(_ => documentClicks++));
        input.PointerMove(20, 20); input.PointerButton(20, 20, true); input.PointerMove(800, 600, 1); input.PointerButton(800, 600, false);
        Assert.That(targetLog, Is.EqualTo(new[] { "mouseup", "click" }));
        Assert.That(documentClicks, Is.Zero);
        Assert.That(b.IsConnected || input.HasPointerCaptureOverride, Is.False);
    }
    [Test]
    public void PointerDownCancellationSuppressesCompatibilityButNotCapturedClick()
    {
        var (doc, input) = Fixture(); using var owner = input;
        var a = doc.GetElementById("a")!; var b = doc.GetElementById("b")!;
        a.AddEventListener("pointerdown", new DelegateEventListener(e => { b.SetPointerCapture(((PointerEvent)e).PointerId); e.PreventDefault(); }));
        int mouse = 0, clicks = 0;
        foreach (string type in new[] { "mousedown", "mousemove", "mouseup" }) doc.AddEventListener(type, new DelegateEventListener(_ => mouse++));
        b.AddEventListener("click", new DelegateEventListener(e => { clicks++; e.PreventDefault(); }));
        input.PointerButton(20, 20, true); input.PointerMove(800, 600, 1); input.PointerButton(800, 600, false);
        Assert.That(mouse, Is.Zero); Assert.That(clicks, Is.EqualTo(1)); Assert.That(doc.FocusedElement, Is.Null);
        Assert.That(input.MoveSequential(false), Is.True);
        Assert.That(doc.FocusedElement, Is.SameAs(b), "Canceled pointerdown still establishes the HTML sequential starting point at A.");
        input.ReleaseInput(); Assert.That(input.MoveSequential(false), Is.True);
        Assert.That(doc.FocusedElement, Is.SameAs(a), "Native context teardown clears the sequential starting point.");
    }
    [Test]
    public void NativeContextReleaseDuringCapturedUpDoesNotGenerateCancelAfterUpOrClick()
    {
        var (doc, input) = Fixture(); using var owner = input;
        var a = doc.GetElementById("a")!; var b = doc.GetElementById("b")!;
        a.AddEventListener("pointerdown", new DelegateEventListener(e => b.SetPointerCapture(((PointerEvent)e).PointerId)));
        var log = new List<string>();
        foreach (string type in new[] { "pointerup", "pointercancel", "lostpointercapture", "click" }) b.AddEventListener(type, new DelegateEventListener(e => log.Add(e.Type)));
        b.AddEventListener("pointerup", new DelegateEventListener(_ => input.ReleaseInput(false)));
        input.PointerButton(20, 20, true); input.PointerMove(800, 600, 1); input.PointerButton(800, 600, false);
        Assert.That(log, Is.EqualTo(new[] { "pointerup", "lostpointercapture" })); Assert.That(a.Matches(":active"), Is.False); Assert.That(input.HasPointerCaptureOverride, Is.False);
    }
}
