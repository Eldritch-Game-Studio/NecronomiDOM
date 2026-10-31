using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    [TestFixture]
    public sealed class DomMutationTests
    {
        private static Document Html() => new Document().Implementation.CreateHTMLDocument("title");
        [Test]
        public void LiveCollectionsTrackMovesAndFragments()
        {
            var doc = Html(); var parent = doc.Body!; var children = parent.ChildNodes; var elements = parent.Children;
            var matches = doc.GetElementsByClassName("row");
            var a = doc.CreateElement("div"); a.ClassName = "row";
            var fragment = doc.CreateDocumentFragment(); fragment.Append(a, "text", doc.CreateElement("span")); parent.AppendChild(fragment);
            Assert.That(fragment.ChildNodes.Length, Is.Zero);
            Assert.That(children.Length, Is.EqualTo(3)); Assert.That(elements.Length, Is.EqualTo(2)); Assert.That(matches.Length, Is.EqualTo(1));
            parent.AppendChild(a); Assert.That(parent.LastChild, Is.SameAs(a));
            a.Remove(); Assert.That(matches.Length, Is.Zero); Assert.That(elements.Length, Is.EqualTo(1));
        }
        [Test]
        public void InvalidDocumentInsertionIsAtomic()
        {
            var doc = Html(); var fragment = doc.CreateDocumentFragment(); fragment.Append(doc.CreateElement("div"), doc.CreateElement("span"));
            Assert.Throws<DomException>(() => doc.AppendChild(fragment)); Assert.That(fragment.ChildNodes.Length, Is.EqualTo(2));
            Assert.Throws<DomException>(() => doc.Body!.AppendChild(doc.DocumentElement!));
            Assert.Throws<DomException>(() => doc.AppendChild(doc.CreateTextNode("text")));
            var replacement = doc.CreateElement("html"); doc.ReplaceChildren(replacement); Assert.That(doc.DocumentElement, Is.SameAs(replacement));
        }
        [Test]
        public void AdoptAndCloneKeepAttributesAndTemplateOwners()
        {
            var doc = Html(); var template = (HTMLTemplateElement)doc.CreateElement("template");
            template.SetAttributeNS("urn:attr", "p:a", "value"); template.Content.Append(doc.CreateElement("b"), "text"); doc.Body!.AppendChild(template);
            Assert.That(template.Content.OwnerDocument, Is.Not.SameAs(doc));
            var copy = (HTMLTemplateElement)template.CloneNode(true);
            Assert.That(copy.GetAttributeNS("urn:attr", "a"), Is.EqualTo("value")); Assert.That(copy.Content.ChildNodes.Length, Is.EqualTo(2));
            var other = Html(); other.AdoptNode(template);
            Assert.That(template.ParentNode, Is.Null); Assert.That(template.OwnerDocument, Is.SameAs(other));
            Assert.That(template.Content.FirstChild!.OwnerDocument, Is.SameAs(template.Content.OwnerDocument));
        }
        [Test]
        public void AttributeOwnershipNamespacesTokensAndDatasetAreLive()
        {
            var doc = Html(); var element = (HTMLElement)doc.CreateElement("div"); var map = element.Attributes;
            var attr = doc.CreateAttributeNS("urn:attr", "p:a"); attr.Value = "one"; element.SetAttributeNodeNS(attr); attr.Value = "two";
            Assert.That(map.Length, Is.EqualTo(1)); Assert.That(element.GetAttributeNS("urn:attr", "a"), Is.EqualTo("two"));
            Assert.Throws<DomException>(() => doc.CreateElement("span").SetAttributeNode(attr));
            Assert.Throws<DomException>(() => element.SetAttributeNS(null, "p:a", "bad"));
            element.ClassList.Add("one", "one", "two"); element.ClassList.Replace("two", "one"); Assert.That(element.ClassName, Is.EqualTo("one"));
            element.Dataset["longName"] = "value"; Assert.That(element.GetAttribute("data-long-name"), Is.EqualTo("value"));
            Assert.Throws<DomException>(() => element.Dataset["a-b"] = "bad");
        }
        [Test]
        public void NormalizeSplitAndPositionUseTreeOrder()
        {
            var doc = Html(); var parent = doc.Body!; var text = doc.CreateTextNode("ab"); parent.Append(text, "", "cd");
            parent.Normalize(); Assert.That(parent.ChildNodes.Length, Is.EqualTo(1)); Assert.That(text.Data, Is.EqualTo("abcd"));
            var suffix = text.SplitText(2); Assert.That(suffix.Data, Is.EqualTo("cd")); Assert.That(text.WholeText, Is.EqualTo("abcd"));
            Assert.That(text.CompareDocumentPosition(suffix), Is.EqualTo(Node.DocumentPositionFollowing));
            Assert.That(parent.CompareDocumentPosition(text), Is.EqualTo(Node.DocumentPositionFollowing | Node.DocumentPositionContainedBy));
            Assert.That(parent.Contains(text), Is.True);
        }
        [Test]
        public void MutationObserversKeepTransientSubtreesUntilCheckpoint()
        {
            var doc = Html(); var parent = doc.Body!; var child = doc.CreateElement("div"); parent.AppendChild(child);
            var delivered = new List<MutationRecord>();
            var observer = new MutationObserver((records, _) => delivered.AddRange(records));
            observer.Observe(parent, new MutationObserverInit { ChildList = true, Attributes = true, AttributeOldValue = true, Subtree = true, AttributeFilter = new[] { "id" } });
            child.Id = "old"; child.Remove(); child.Id = "detached"; child.ClassName = "filtered";
            MutationObserver.NotifyAll(doc);
            Assert.That(delivered.Count, Is.EqualTo(3)); Assert.That(delivered[2].OldValue, Is.EqualTo("old"));
            child.Id = "after"; Assert.That(observer.TakeRecords(), Is.Empty);
            observer.Disconnect(); parent.AppendChild(child); Assert.That(observer.TakeRecords(), Is.Empty);
        }
        [Test]
        public void ChangeSinkReceivesAllMutationKinds()
        {
            var doc = Html(); var sink = new Sink(); doc.ChangeSink = sink;
            Assert.That(((IDomChangeSink)sink).RequiresAttributeOldValue, Is.True);
            var element = doc.CreateElement("div"); doc.Body!.AppendChild(element); element.Id = "id"; element.Id = "next";
            var text = doc.CreateTextNode("a"); element.AppendChild(text); text.Data = "b"; element.Remove();
            Assert.That(sink.Calls, Does.Contain("insert").And.Contain("remove").And.Contain("children").And.Contain("attribute").And.Contain("text"));
            Assert.That(sink.OldValues, Is.EqualTo(new string?[] { null, "id" }));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void DetachedLazyAttributeFreezesValueAndMutationOldValue(bool replace)
        {
            var doc = Html(); var element = doc.CreateElement("div");
            var attr = doc.CreateAttribute("data-lazy"); element.SetAttributeNodeNS(attr);
            var current = "first"; attr.ValueProvider = () => current;
            var observer = new MutationObserver((_, _) => { });
            observer.Observe(element, new MutationObserverInit { Attributes = true, AttributeOldValue = true });
            if (replace)
            {
                var replacement = doc.CreateAttribute("data-lazy"); replacement.Value = "replacement";
                Assert.That(element.SetAttributeNodeNS(replacement), Is.SameAs(attr));
            }
            else Assert.That(element.RemoveAttributeNode(attr), Is.SameAs(attr));
            current = "second";
            Assert.That(attr.OwnerElement, Is.Null); Assert.That(attr.Value, Is.EqualTo("first"));
            Assert.That((object?)attr.ValueProvider, Is.Null);
            var records = observer.TakeRecords();
            Assert.That(records.Count, Is.EqualTo(1)); Assert.That(records[0].OldValue, Is.EqualTo("first"));
        }
        private sealed class Sink : IDomChangeSink
        {
            internal readonly List<string> Calls = new();
            internal readonly List<string?> OldValues = new();
            public void ChildrenChanged(Node parent) => Calls.Add("children");
            public void AttributeChanged(Element element, string localName, string? oldValue) { Calls.Add("attribute"); OldValues.Add(oldValue); }
            public void CharacterDataChanged(CharacterData node) => Calls.Add("text");
            public void NodeInserted(Node node) => Calls.Add("insert");
            public void NodeRemoved(Node node, Node oldParent) => Calls.Add("remove");
        }
    }
}
