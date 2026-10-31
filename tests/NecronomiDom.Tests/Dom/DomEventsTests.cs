using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    [TestFixture]
    public sealed class DomEventsTests
    {
        [Test]
        public void DispatchCapturesBubblesAndHonorsOncePassiveCancellation()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var parent = doc.Body!; var child = doc.CreateElement("div"); parent.AppendChild(child);
            var log = new List<string>();
            parent.AddEventListener("x", new DelegateEventListener(e => log.Add("capture")), true);
            parent.AddEventListener("x", new DelegateEventListener(e => log.Add("bubble")));
            child.AddEventListener("x", new DelegateEventListener(e => { log.Add("once"); e.PreventDefault(); }), new EventListenerOptions { Once = true, Passive = true });
            var cancel = new DelegateEventListener(e => { log.Add("cancel"); e.ReturnValue = false; }); child.AddEventListener("x", cancel);
            var first = new Event("x", new EventInit { Bubbles = true, Cancelable = true });
            Assert.That(child.DispatchEvent(first), Is.False); Assert.That(first.DefaultPrevented, Is.True);
            Assert.That(log, Is.EqualTo(new[] { "capture", "once", "cancel", "bubble" }));
            child.RemoveEventListener("x", cancel); log.Clear();
            Assert.That(child.DispatchEvent(new Event("x", new EventInit { Bubbles = true, Cancelable = true })), Is.True);
            Assert.That(log, Is.EqualTo(new[] { "capture", "bubble" }));
            Assert.That(first.CurrentTarget, Is.Null); Assert.That(first.ComposedPath(), Is.Empty);
        }
        [Test]
        public void ClosedShadowRetargetsAndHidesComposedPath()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var host = doc.CreateElement("div"); doc.Body!.AppendChild(host);
            var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Closed }); var child = doc.CreateElement("span"); shadow.AppendChild(child);
            EventTarget? target = null; IReadOnlyList<EventTarget>? path = null;
            host.AddEventListener("x", new DelegateEventListener(e => { target = e.Target; path = e.ComposedPath(); }));
            child.DispatchEvent(new Event("x", new EventInit { Bubbles = true, Composed = true }));
            Assert.That(host.ShadowRoot, Is.Null); Assert.That(target, Is.SameAs(host)); Assert.That(path, Does.Not.Contain(child).And.Not.Contain(shadow));
            target = null; child.DispatchEvent(new Event("x", new EventInit { Bubbles = true })); Assert.That(target, Is.Null);
        }
        [Test]
        public void NamedAndManualSlotsAssignFlattenAndSignalAtCheckpoint()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var host = doc.CreateElement("div"); doc.Body!.AppendChild(host);
            var root = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open }); var slot = (HTMLSlotElement)doc.CreateElement("slot"); slot.Name = "row"; root.AppendChild(slot);
            var child = doc.CreateElement("span"); child.Slot = "row"; host.AppendChild(child);
            var signals = 0; slot.AddEventListener("slotchange", new DelegateEventListener(_ => signals++));
            Assert.That(slot.AssignedNodes(), Is.EqualTo(new[] { child })); Assert.That(child.AssignedSlot, Is.SameAs(slot));
            MutationObserver.NotifyAll(doc); Assert.That(signals, Is.EqualTo(1));
            child.Slot = "other"; Assert.That(slot.AssignedNodes(), Is.Empty); MutationObserver.NotifyAll(doc); Assert.That(signals, Is.EqualTo(2));
            var manualHost = doc.CreateElement("div"); var manualRoot = manualHost.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open, SlotAssignment = SlotAssignmentMode.Manual });
            var manualSlot = (HTMLSlotElement)doc.CreateElement("slot"); manualRoot.AppendChild(manualSlot); manualHost.AppendChild(child); manualSlot.Assign(child);
            Assert.That(manualSlot.AssignedElements(), Is.EqualTo(new[] { child }));
        }
        [Test]
        public void RegistryUpgradesExistingIdentityAndDeliversObservedLifecycle()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var child = doc.CreateElement("x-test"); child.Id = "before"; doc.Body!.AppendChild(child);
            var log = new List<string>();
            doc.CustomElements.Define("x-test", new CustomElementDefinition((d, name) => new TestElement(d, name), new[] { "id" },
                connected: e => log.Add("connected"), disconnected: e => log.Add("disconnected"), adopted: (e, old, current) => log.Add("adopted"),
                attributeChanged: (e, name, old, value, ns) => log.Add(name + ":" + old + ":" + value), upgraded: e => log.Add("upgrade")));
            Assert.That(child.IsDefined, Is.True); Assert.That(doc.GetElementById("before"), Is.SameAs(child));
            Assert.That(log, Is.EqualTo(new[] { "upgrade", "id::before", "connected" }));
            child.Id = "after"; child.ClassName = "unobserved"; child.Remove();
            var other = new Document().Implementation.CreateHTMLDocument(); other.AdoptNode(child); other.Body!.AppendChild(child);
            Assert.That(log, Is.EqualTo(new[] { "upgrade", "id::before", "connected", "id:before:after", "disconnected", "adopted", "connected" }));
            Assert.That(doc.CreateElement("x-test"), Is.TypeOf<TestElement>());
            Assert.That(doc.CustomElements.WhenDefined("x-test").IsCompletedSuccessfully, Is.True);
        }
        [Test]
        public void ClonableShadowRootsAreCopiedButOrdinaryRootsAreNot()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var host = doc.CreateElement("div");
            var root = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open, Clonable = true, Serializable = true }); root.Append("shadow");
            var copy = (Element)host.CloneNode(true); Assert.That(copy.ShadowRoot!.TextContent, Is.EqualTo("shadow")); Assert.That(copy.ShadowRoot, Is.Not.SameAs(root));
            Assert.Throws<DomException>(() => root.CloneNode());
        }
        private sealed class TestElement : HTMLElement
        { internal TestElement(Document doc, string name) : base(doc, name) { } }
    }
}
