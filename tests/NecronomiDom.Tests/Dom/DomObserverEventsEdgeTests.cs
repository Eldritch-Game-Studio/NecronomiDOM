using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    [TestFixture]
    public sealed class DomObserverEventsEdgeTests
    {
        [Test]
        public void ObserverMutationIsDeliveredAtNextExplicitCheckpoint()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var target = doc.Body!; var batches = new List<int>();
            var observer = new MutationObserver((records, _) => { batches.Add(records.Count); if (batches.Count == 1) target.Id = "second"; });
            observer.Observe(target, new MutationObserverInit { AttributeOldValue = true }); target.Id = "first";
            MutationObserver.NotifyAll(doc); Assert.That(batches, Is.EqualTo(new[] { 1 }));
            MutationObserver.NotifyAll(doc); Assert.That(batches, Is.EqualTo(new[] { 1, 1 }));
        }
        [Test]
        public void ObserverOptionsValidatePresenceAndCopyAttributeFilters()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var filter = new List<string> { "id" };
            var observer = new MutationObserver((_, _) => { });
            Assert.Throws<DomTypeError>(() => observer.Observe(doc.Body!, new MutationObserverInit()));
            Assert.Throws<DomTypeError>(() => observer.Observe(doc.Body!, new MutationObserverInit { Attributes = false, AttributeOldValue = true }));
            observer.Observe(doc.Body!, new MutationObserverInit { AttributeFilter = filter }); filter.Clear(); doc.Body!.Id = "x";
            Assert.That(observer.TakeRecords().Count, Is.EqualTo(1));
        }
        [Test]
        public void AbortRemovesListenersAndExposesCorrectLegacyExceptionName()
        {
            var controller = new AbortController(); var target = new EventTarget(); var calls = 0;
            target.AddEventListener("x", new DelegateEventListener(_ => calls++), new EventListenerOptions { Signal = controller.Signal });
            target.DispatchEvent(new Event("x")); controller.Abort(); target.DispatchEvent(new Event("x")); Assert.That(calls, Is.EqualTo(1));
            var error = Assert.Throws<DomException>(() => controller.Signal.ThrowIfAborted())!;
            Assert.That(error.Name, Is.EqualTo("AbortError")); Assert.That(error.Code, Is.EqualTo(20));
        }
        [Test]
        public void ListenerReportingFailureDoesNotLeaveEventDispatching()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var target = doc.Body!;
            var listener = new DelegateEventListener(_ => throw new InvalidOperationException("listener")); target.AddEventListener("x", listener);
            doc.ListenerExceptionHandler = _ => throw new InvalidOperationException("reporting"); var e = new Event("x");
            Assert.Throws<AggregateException>(() => target.DispatchEvent(e)); Assert.That(e.CurrentTarget, Is.Null); Assert.That(e.EventPhase, Is.EqualTo(Event.None));
            target.RemoveEventListener("x", listener); Assert.That(target.DispatchEvent(e), Is.True);
        }
        [Test]
        public void NormalizeQueuesOneCharacterDataRecordForEntireTextRun()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var parent = doc.Body!;
            parent.Append("a", "b", "c");
            var observer = new MutationObserver((_, _) => { });
            observer.Observe(parent, new MutationObserverInit { CharacterDataOldValue = true, Subtree = true, ChildList = true });
            parent.Normalize(); var records = observer.TakeRecords();
            Assert.That(records.Count, Is.EqualTo(3));
            Assert.That(records[0].Type, Is.EqualTo("characterData")); Assert.That(records[0].OldValue, Is.EqualTo("a"));
            Assert.That(parent.TextContent, Is.EqualTo("abc"));
        }
    }
}
