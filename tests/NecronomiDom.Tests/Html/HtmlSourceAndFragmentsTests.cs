using System;
using System.Linq;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Html
{
    [TestFixture]
    public sealed class HtmlSourceAndFragmentsTests
    {
        private static readonly WebUrl Url = WebUrl.Parse("res:///ui/source.html")!;
        [Test]
        public void WarningPositionsDoNotWrapAfterUshortLineLimit()
        {
            var html = "<!doctype html>" + new string('\n', 70000) + "</unknown>";
            var result = HtmlImporter.Import(html, "large.html", Url);
            Assert.That(result.Diagnostics.Any(d => d.Line == 70001), Is.True);
        }
        [Test]
        public void ElementSourceLocationRetainsOriginalOffsetAndCrLfLines()
        {
            const string html = "<!doctype html>\r\n<div id=located>Ω</div>";
            var element = HtmlImporter.Import(html, "location.html", Url).Document.GetElementById("located")!;
            Assert.That(element.SourceLocation, Is.Not.Null);
            Assert.That(element.SourceLocation!.Value.Offset, Is.EqualTo(html.IndexOf("<div", StringComparison.Ordinal)));
            Assert.That(element.SourceLocation.Value.Line, Is.EqualTo(2)); Assert.That(element.SourceLocation.Value.Column, Is.EqualTo(1));
        }
        [Test]
        public void ContextFragmentsUseNamespaceAndDoNotProcessDeclarativeShadowTemplates()
        {
            var document = new Document().Implementation.CreateHTMLDocument(); var context = document.CreateElement("div");
            var fragment = HtmlImporter.ImportFragment("<div><template shadowrootmode=open><b>x</b></template></div>", context, "fragment.html").Fragment;
            var host = (Element)fragment.FirstChild!; Assert.That(host.ShadowRoot, Is.Null); Assert.That(host.FirstChild, Is.TypeOf<HTMLTemplateElement>());
            var svg = document.CreateElementNS("http://www.w3.org/2000/svg", "svg");
            var foreign = HtmlImporter.ImportFragment("<g><path/></g>", svg, "svg-fragment.html").Fragment;
            Assert.That(((Element)foreign.FirstChild!).NamespaceURI, Is.EqualTo("http://www.w3.org/2000/svg"));
        }
        [Test]
        public void MathFragmentContextRetainsEncodingAttributeForHtmlIntegration()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            var context = document.CreateElementNS("http://www.w3.org/1998/Math/MathML", "annotation-xml");
            context.SetAttribute("encoding", "text/html");
            var fragment = HtmlImporter.ImportFragment("<foo></foo>", context, "math-fragment.html").Fragment;
            Assert.That(((Element)fragment.FirstChild!).NamespaceURI, Is.EqualTo("http://www.w3.org/1999/xhtml"));
            context.SetAttribute("encoding", "application/xml");
            var foreign = HtmlImporter.ImportFragment("<foo></foo>", context, "math-foreign-fragment.html").Fragment;
            Assert.That(((Element)foreign.FirstChild!).NamespaceURI, Is.EqualTo("http://www.w3.org/1998/Math/MathML"));
        }
        [Test]
        public void AttributedRegistryUsesExplicitCSharpLifecycleDefinition()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            document.CustomElements.Define<NamedElement>(new CustomElementDefinition((d, name) => new NamedElement(d, name)));
            Assert.That(document.CreateElement("x-named"), Is.TypeOf<NamedElement>());
        }
        [CustomElement("x-named")]
        private sealed class NamedElement : HTMLElement
        { internal NamedElement(Document doc, string name) : base(doc, name) { } }
    }
}
