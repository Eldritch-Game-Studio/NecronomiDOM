using System;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    [TestFixture]
    public sealed class CustomNameTests
    {
        [TestCase("missing-glyph")]
        [TestCase("No-hyphen")]
        [TestCase("withoutdash")]
        [TestCase("x-Upper")]
        [TestCase("x-\uE000")]
        [TestCase("x-\uFFFF")]
        public void RegistryRejectsReservedOrInvalidPcenNames(string name)
        {
            var doc = new Document().Implementation.CreateHTMLDocument();
            Assert.Throws<DomException>(() => doc.CustomElements.Define(name, Definition()));
        }
        [TestCase(0xD800)]
        [TestCase(0xDFFF)]
        public void RegistryRejectsUnpairedSurrogatePcenNames(int codeUnit)
        {
            // Keep lone surrogates out of custom-attribute string transport, which repairs them.
            var name = new string(new[] { 'k', 'k', '-', (char)codeUnit });
            var doc = new Document().Implementation.CreateHTMLDocument();
            Assert.Throws<DomException>(() => doc.CustomElements.Define(name, Definition()));
            Assert.Throws<DomException>(() => doc.CreateElement(name));
        }
        [TestCase("x-good")]
        [TestCase("x-\u00B7")]
        [TestCase("x-\U00020000")]
        public void RegistryAcceptsCompletePcenUnicodeRanges(string name)
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); doc.CustomElements.Define(name, Definition());
            Assert.That(doc.CreateElement(name).IsDefined, Is.True);
        }
        [Test]
        public void HtmlAsciiCaseFoldingPreservesExactNonAsciiRegistryName()
        {
            var doc = new Document().Implementation.CreateHTMLDocument(); doc.CustomElements.Define("x-Ā", Definition());
            var element = doc.CreateElement("X-Ā"); doc.Body!.AppendChild(element);
            Assert.That(element, Is.TypeOf<TestElement>()); Assert.That(element.IsDefined, Is.True);
            Assert.That(element.LocalName, Is.EqualTo("x-Ā")); Assert.That(element.TagName, Is.EqualTo("X-Ā"));
            Assert.That(doc.CreateElement("x-ā").IsDefined, Is.False);
            Assert.That(doc.CreateElement("x-ā").TagName, Is.EqualTo("X-ā"));
            element.SetAttribute("DATA-Ā", "exact");
            Assert.That(element.GetAttribute("data-Ā"), Is.EqualTo("exact"));
            Assert.That(element.GetAttribute("data-ā"), Is.Null);
            Assert.That(doc.CreateAttribute("DATA-Ā").Name, Is.EqualTo("data-Ā"));
            Assert.That(doc.GetElementsByTagName("X-Ā").Item(0), Is.SameAs(element));
            Assert.That(doc.GetElementsByTagName("X-ā").Length, Is.Zero);
        }
        [TestCase(0x10000)]
        [TestCase(0x20000)]
        [TestCase(0xEFFFF)]
        public void XmlFactoriesAcceptSupplementaryNameScalars(int codePoint)
        {
            var name = char.ConvertFromUtf32(codePoint); var doc = new Document();
            Assert.That(doc.CreateElement(name).LocalName, Is.EqualTo(name));
            Assert.That(doc.CreateAttribute(name).LocalName, Is.EqualTo(name));
            Assert.That(doc.CreateProcessingInstruction(name, "").Target, Is.EqualTo(name));
            var qualified = name + ":" + name;
            var element = doc.CreateElementNS("urn:name", qualified);
            Assert.That(element.Prefix, Is.EqualTo(name)); Assert.That(element.LocalName, Is.EqualTo(name));
            Assert.That(doc.CreateAttributeNS("urn:name", qualified).Name, Is.EqualTo(qualified));
            Assert.That(doc.Implementation.CreateDocumentType(qualified).Name, Is.EqualTo(qualified));
        }
        [TestCase(0xFFFF)]
        [TestCase(0xF0000)]
        [TestCase(0x10FFFF)]
        public void XmlFactoriesRejectScalarsOutsideNameGrammar(int codePoint)
        {
            var name = "a" + char.ConvertFromUtf32(codePoint); var doc = new Document();
            Assert.Throws<DomException>(() => doc.CreateElement(name));
            Assert.Throws<DomException>(() => doc.CreateAttributeNS("urn:name", "p:" + name));
        }
        private static CustomElementDefinition Definition() => new((doc, name) => new TestElement(doc, name));
        private sealed class TestElement : HTMLElement
        { internal TestElement(Document doc, string name) : base(doc, name) { } }
    }
}
