using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    [TestFixture]
    public sealed class DomEdgeTests
    {
        [Test]
        public void SelfInsertionOfDocumentDoctypePreservesDocumentValidity()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var doctype = doc.Doctype!;
            Assert.That(doc.InsertBefore(doctype, doctype), Is.SameAs(doctype)); Assert.That(doc.FirstChild, Is.SameAs(doctype));
            Assert.That(doc.ChildElementCount, Is.EqualTo(1));
        }
        [Test]
        public void NormalizeDoesNotMergeOrRemoveCdataSections()
        {
            var doc = new Document(); var root = doc.CreateElement("root"); doc.AppendChild(root);
            var empty = doc.CreateCDATASection(""); var cdata = doc.CreateCDATASection("cdata");
            root.Append("a", empty, cdata, "b"); root.Normalize();
            Assert.That(root.ChildNodes.Length, Is.EqualTo(4)); Assert.That(root.TextContent, Is.EqualTo("acdatab"));
        }
        [Test]
        public void AttributePositionUsesOwnerOrderingButContainsDoesNot()
        {
            var doc = new Document(); var element = doc.CreateElement("root"); element.SetAttribute("a", "1"); element.SetAttribute("b", "2");
            var a = element.GetAttributeNode("a")!; var b = element.GetAttributeNode("b")!;
            Assert.That(a.CompareDocumentPosition(b), Is.EqualTo(Node.DocumentPositionFollowing | Node.DocumentPositionImplementationSpecific));
            Assert.That(a.CompareDocumentPosition(element), Is.EqualTo(Node.DocumentPositionPreceding | Node.DocumentPositionContains));
            Assert.That(element.Contains(a), Is.False);
        }
        [Test]
        public void EqualTokenReplacementAndInvalidDatasetReadDoNotMutateIncorrectly()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var element = (HTMLElement)doc.CreateElement("div"); element.ClassName = " one  one ";
            Assert.That(element.ClassList.Replace("one", "one"), Is.True); Assert.That(element.ClassName, Is.EqualTo("one"));
            element.SetAttribute("data-a-b", "value"); Assert.That(element.Dataset["aB"], Is.EqualTo("value"));
            Assert.That(element.Dataset["a-b"], Is.Null); Assert.That(element.Dataset.Remove("a-b"), Is.False);
        }
        [Test]
        public void HtmlNamesAndEnumeratedAttributesFoldOnlyAsciiLetters()
        {
            var doc = new Document().Implementation.CreateHTMLDocument();
            var element = (HTMLElement)doc.CreateElement("DiV");
            Assert.That(element.LocalName, Is.EqualTo("div")); Assert.That(element.TagName, Is.EqualTo("DIV"));
            element.SetAttribute("DATA-Ω", "x"); Assert.That(element.Attributes.Item(0)!.Name, Is.EqualTo("data-Ω"));
            var parent = (HTMLElement)doc.CreateElement("div"); parent.Translate = false; parent.AppendChild(element);
            element.SetAttribute("translate", "yeſ"); Assert.That(element.Translate, Is.False);
            element.SetAttribute("translate", "YES"); Assert.That(element.Translate, Is.True);
            var nonAscii = doc.CreateElement("Ω"); Assert.That(nonAscii.LocalName, Is.EqualTo("Ω")); Assert.That(nonAscii.TagName, Is.EqualTo("Ω"));
        }
        [Test]
        public void KeywordAndQuirksClassComparisonDoesNotApplyUnicodeCaseFolding()
        {
            var doc = new Document().Implementation.CreateHTMLDocument();
            var input = (HTMLInputElement)doc.CreateElement("input"); input.Type = "paſſword";
            Assert.That(input.Type, Is.EqualTo("text")); input.Type = "PASSWORD"; Assert.That(input.Type, Is.EqualTo("password"));
            var first = doc.CreateElement("div"); first.ClassName = "ſ";
            var second = doc.CreateElement("div"); second.ClassName = "S"; doc.Body!.Append(first, second);
            doc.CompatMode = "BackCompat";
            Assert.That(doc.GetElementsByClassName("s").Length, Is.EqualTo(1));
            Assert.That(doc.GetElementsByClassName("s").Item(0), Is.SameAs(second));
        }
        [Test]
        public void CustomReactionRunsAfterWholeFragmentInsertion()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var observedCounts = new List<int>();
            doc.CustomElements.Define("x-row", new CustomElementDefinition((d, name) => new Row(d, name), connected: _ => observedCounts.Add(doc.Body!.ChildElementCount)));
            var fragment = doc.CreateDocumentFragment(); fragment.Append(doc.CreateElement("x-row"), doc.CreateElement("x-row")); doc.Body!.AppendChild(fragment);
            Assert.That(observedCounts, Is.EqualTo(new[] { 2, 2 }));
        }
        [Test]
        public void FailedUpgradeStopsLaterLifecycleAndCannotRetry()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); var element = doc.CreateElement("x-fail"); element.Id = "existing"; doc.Body!.AppendChild(element);
            var later = 0; doc.ListenerExceptionHandler = _ => { };
            doc.CustomElements.Define("x-fail", new CustomElementDefinition((d, name) => new Row(d, name), new[] { "id" }, connected: _ => later++, attributeChanged: (e, n, o, v, ns) => later++, upgraded: _ => throw new InvalidOperationException("upgrade failure")));
            Assert.That(element.IsDefined, Is.False); Assert.That(later, Is.Zero);
            doc.CustomElements.Upgrade(element); element.Id = "after"; Assert.That(later, Is.Zero);
        }
        private sealed class Row : HTMLElement { internal Row(Document doc, string name) : base(doc, name) { } }
    }
}
