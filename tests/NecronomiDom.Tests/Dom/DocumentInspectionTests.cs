using System.Linq;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    [Test]
    public void InspectorPicksPointerEventsNoneWithoutDispatchingOrActivatingDocumentInput()
    {
        var (document, input, display) = Fixture("<button id=target>Inspect me</button>", "#target{width:100px;height:30px;pointer-events:none}");
        using var owner = input;
        int events = 0;
        document.AddEventListener("click", new DelegateEventListener(_ => events++));
        var target = document.GetElementById("target");
        Assert.That(display.HitTest(10, 10), Is.Not.SameAs(target), "DOM input excludes pointer-events:none");
        Assert.That(DocumentInspection.Pick(display, 10, 10), Is.SameAs(target));
        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Zero);
            Assert.That(input.PointerActivationCount, Is.Zero);
            Assert.That(input.InspectState().PointerDown, Is.False);
            Assert.That(document.FocusedElement, Is.Null);
        });
    }

    [Test]
    public void InspectorPickingUsesActualTransformedOverflowClipsAndSkipsDetachedStaleBoxes()
    {
        var (document, input, display) = Fixture("<div id=frame><div id=child></div></div>", "#frame{position:absolute;left:40px;top:40px;transform:translate(20px,10px);width:80px;height:80px;overflow:hidden}#child{width:120px;height:80px;background:red}");
        using var owner = input;
        var child = document.GetElementById("child");
        Assert.That(DocumentInspection.Pick(display, 65, 55), Is.SameAs(child));
        Assert.That(DocumentInspection.Pick(display, 150, 55), Is.Not.SameAs(child));
        child!.Remove();
        Assert.That(DocumentInspection.Pick(display, 65, 55), Is.Not.SameAs(child));
    }

    [Test]
    public void InputInspectionObservesPendingAndAppliedCaptureWithoutProcessingIt()
    {
        var (document, input, _) = Fixture("<button id=target>A</button>", "#target{width:100px;height:30px}");
        using var owner = input;
        var target = document.GetElementById("target")!;
        input.PointerMove(10, 10); input.PointerButton(10, 10, true);
        target.SetPointerCapture(1);
        var pending = input.InspectState();
        Assert.Multiple(() =>
        {
            Assert.That(pending.PointerDown, Is.True);
            Assert.That(pending.Pressed, Is.SameAs(target));
            Assert.That(pending.PendingCapture, Is.SameAs(target));
            Assert.That(pending.Captured, Is.Null, "snapshot must not flush pending capture");
        });
        input.PointerMove(12, 12, 1);
        Assert.That(input.InspectState().Captured, Is.SameAs(target));
        input.ReleaseInput();
        var released = input.InspectState();
        Assert.Multiple(() =>
        {
            Assert.That(released.PointerDown, Is.False);
            Assert.That(released.Pressed, Is.Null);
            Assert.That(released.Captured, Is.Null);
            Assert.That(input.PointerActivationCount, Is.Zero, "suspension cannot synthesize a click");
        });
    }
}

[TestFixture]
public sealed class DocumentInspectionTests
{
    [Test]
    public void AncestryCrossesShadowRootThroughItsHostAndRetainsSelectedFirst()
    {
        var document = new Document().Implementation.CreateHTMLDocument("inspection");
        var host = document.CreateElement("div"); host.Id = "host"; document.Body!.AppendChild(host);
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var child = document.CreateElement("span"); child.Id = "child"; child.SetAttribute("class", "alpha  beta"); shadow.AppendChild(child);
        Assert.That(DocumentInspection.Ancestry(child).Take(3), Is.EqualTo(new[] { child, host, document.Body }));
        Assert.That(DocumentInspection.Label(child), Is.EqualTo("span#child.alpha.beta"));
    }
}
