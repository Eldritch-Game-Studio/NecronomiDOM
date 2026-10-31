using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    [TestFixture]
    public sealed class CustomStateTests
    {
        [Test]
        public void InternalsRequireEligibleCustomStateAndAttachOnlyOnce()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            AssertNotSupported(() => document.Body!.AttachInternals());
            var pending = (HTMLElement)document.CreateElement("x-state");
            AssertNotSupported(() => pending.AttachInternals());
            document.CustomElements.Define("x-state", Definition());
            AssertNotSupported(() => pending.AttachInternals());
            document.CustomElements.Upgrade(pending);
            var internals = pending.AttachInternals();
            Assert.That(internals.States, Is.SameAs(internals.States));
            AssertNotSupported(() => pending.AttachInternals());
        }
        [Test]
        public void DefinitionCanDisableInternalsWithoutInventingFormApis()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            document.CustomElements.Define("x-disabled", new CustomElementDefinition((doc, name) => new StateElement(doc, name), disableInternals: true));
            AssertNotSupported(() => ((HTMLElement)document.CreateElement("x-disabled")).AttachInternals());
        }
        [Test]
        public void FactoryConstructorCanAttachPrecustomizedInternals()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            document.CustomElements.Define("x-constructor", Definition(true));
            var element = (StateElement)document.CreateElement("x-constructor");
            Assert.That(element.DefinedDuringConstruction, Is.False);
            Assert.That(element.IsDefined, Is.True);
            Assert.That(element.ConstructorInternals!.States.Has("initial"), Is.True);
            Assert.That(element.HasCustomState("initial"), Is.True);
            AssertNotSupported(() => element.AttachInternals());
        }
        [Test]
        public void UpgradeCallbackCanAttachInternalsToExistingIdentity()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            var element = (HTMLElement)document.CreateElement("x-upgrade"); document.Body!.AppendChild(element);
            ElementInternals? internals = null;
            document.CustomElements.Define("x-upgrade", new CustomElementDefinition((doc, name) => new StateElement(doc, name), upgraded: node => internals = ((HTMLElement)node).AttachInternals()));
            internals!.States.Add("ready");
            Assert.That(element.HasCustomState("ready"), Is.True);
            Assert.That(document.Body!.FirstChild, Is.SameAs(element));
        }
        [Test]
        public void FailedFactoryElementCannotExposeStatesOrAttachAgain()
        {
            var document = new Document().Implementation.CreateHTMLDocument(); StateElement? failed = null;
            document.CustomElements.Define("x-failed", new CustomElementDefinition((doc, name) => { failed = new StateElement(doc, name, true); throw new InvalidOperationException("construction failed"); }));
            Assert.Throws<InvalidOperationException>(() => document.CreateElement("x-failed"));
            var target = failed ?? throw new InvalidOperationException("Factory did not create its captured element.");
            Assert.That(target.IsDefined, Is.False);
            Assert.That(target.HasCustomState("initial"), Is.False);
            AssertNotSupported(() => target.AttachInternals());
        }
        [Test]
        public void IsContentAttributeDoesNotConvertAnAutonomousElementIntoACustomizedBuiltIn()
        {
            var element = Element(); element.SetAttribute("is", "different-name");
            element.AttachInternals().States.Add("ready");
            Assert.That(element.HasCustomState("ready"), Is.True);
        }
        [Test]
        public void StateSetPreservesExactDomStringsRatherThanLegacyIdentifierRestrictions()
        {
            var states = Element().AttachInternals().States;
            var values = new[] { "", "1ready", "two words", "initial", "none", "--old", "\\x", "ſ", "S", "\0", new string(new[] { (char)0xD800 }) };
            foreach (var value in values) Assert.That(states.Add(value), Is.SameAs(states));
            Assert.That(states.Count, Is.EqualTo(values.Length));
            Assert.That(states.ToArray(), Is.EqualTo(values));
            Assert.That(states.Has("s"), Is.False);
            foreach (var value in values) Assert.That(states.Has(value), Is.True);
        }
        [Test]
        public void OnlyActualStateChangesInvalidateTheCurrentDocument()
        {
            var element = Element(); var states = element.AttachInternals().States; var sink = new Sink(); element.NodeDocument.ChangeSink = sink;
            states.Add("busy"); states.Add("busy"); states.Add("Busy");
            Assert.That(sink.Changes.Count, Is.EqualTo(2));
            Assert.That(element.HasCustomState("busy"), Is.True);
            Assert.That(states.Delete("missing"), Is.False);
            Assert.That(states.Delete("busy"), Is.True);
            states.Clear(); states.Clear();
            Assert.That(sink.Changes.Count, Is.EqualTo(4));
            Assert.That(sink.Changes.All(change => ReferenceEquals(change.Element, element) && change.Name == ":state"), Is.True);
            Assert.That(element.HasCustomState("Busy"), Is.False);
            Assert.That(element.HasAttributes(), Is.False);
        }
        [Test]
        public void IterationSkipsDeletedValuesAndVisitsAppendsAndReinsertions()
        {
            var states = Element().AttachInternals().States; states.Add("a").Add("b").Add("c");
            using var iterator = states.GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.True); Assert.That(iterator.Current, Is.EqualTo("a"));
            states.Delete("b"); states.Add("d");
            Assert.That(iterator.MoveNext(), Is.True); Assert.That(iterator.Current, Is.EqualTo("c"));
            states.Delete("a"); states.Add("a");
            Assert.That(iterator.MoveNext(), Is.True); Assert.That(iterator.Current, Is.EqualTo("d"));
            Assert.That(iterator.MoveNext(), Is.True); Assert.That(iterator.Current, Is.EqualTo("a"));
            Assert.That(iterator.MoveNext(), Is.False); states.Add("e"); Assert.That(iterator.MoveNext(), Is.False);
            Assert.That(states.ToArray(), Is.EqualTo(new[] { "c", "d", "a", "e" }));
        }
        [Test]
        public void ClearAndForEachHaveLiveSetlikeSemantics()
        {
            var states = Element().AttachInternals().States; states.Add("a").Add("b");
            using var iterator = states.GetEnumerator(); Assert.That(iterator.MoveNext(), Is.True);
            states.Clear(); states.Add("x");
            Assert.That(iterator.MoveNext(), Is.True); Assert.That(iterator.Current, Is.EqualTo("x"));
            var visited = new List<string>();
            states.ForEach((value, key, set) => { Assert.That(key, Is.EqualTo(value)); Assert.That(set, Is.SameAs(states)); visited.Add(value); if (value == "x") set.Add("y"); });
            Assert.That(visited, Is.EqualTo(new[] { "x", "y" }));
            Assert.That(states.Keys().ToArray(), Is.EqualTo(states.Values().ToArray()));
            Assert.That(states.Entries().Select(pair => pair.Key == pair.Value).All(equal => equal), Is.True);
        }
        [Test]
        public void AdoptedStatesRetainIdentityAndInvalidateTheirNewDocument()
        {
            var element = Element(); var states = element.AttachInternals().States; states.Add("old");
            var oldDocument = element.NodeDocument; var current = new Document(); current.AdoptNode(element);
            var oldSink = new Sink(); var newSink = new Sink(); oldDocument.ChangeSink = oldSink; current.ChangeSink = newSink;
            states.Add("current");
            Assert.That(element.HasCustomState("old"), Is.True); Assert.That(element.HasCustomState("current"), Is.True);
            Assert.That(oldSink.Changes, Is.Empty); Assert.That(newSink.Changes.Count, Is.EqualTo(1));
        }
        private static StateElement Element()
        {
            var document = new Document().Implementation.CreateHTMLDocument(); document.CustomElements.Define("x-state", Definition());
            return (StateElement)document.CreateElement("x-state");
        }
        private static CustomElementDefinition Definition(bool attachInConstructor = false) => new((doc, name) => new StateElement(doc, name, attachInConstructor));
        private static void AssertNotSupported(Action action) => Assert.That(Assert.Throws<DomException>(action)!.Name, Is.EqualTo("NotSupportedError"));
        private sealed class StateElement : HTMLElement
        {
            internal StateElement(Document doc, string name, bool attach = false) : base(doc, name)
            { DefinedDuringConstruction = IsDefined; if (attach) { ConstructorInternals = AttachInternals(); ConstructorInternals.States.Add("initial"); } }
            internal bool DefinedDuringConstruction { get; }
            internal ElementInternals? ConstructorInternals { get; }
        }
        private sealed class Sink : IDomChangeSink
        {
            internal readonly List<(Element Element, string Name)> Changes = new();
            public void AttributeChanged(Element element, string name, string? oldValue) => Changes.Add((element, name));
            public void ChildrenChanged(Node parent) { }
            public void CharacterDataChanged(CharacterData node) { }
            public void NodeInserted(Node node) { }
            public void NodeRemoved(Node node, Node oldParent) { }
        }
    }
}
