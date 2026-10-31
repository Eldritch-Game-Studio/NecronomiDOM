using System;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Html
{
    [TestFixture]
    public sealed class HtmlImportTests
    {
        private static readonly WebUrl Url = WebUrl.Parse("res:///ui/test.html")!;
        [Test]
        public void ImportPreservesBrowserRecoveryAndWarningPositions()
        {
            var result = HtmlImporter.Import("<!doctype html><p>one<div>two</div>three", "screen.html", Url);
            Assert.That(result.Document.CompatMode, Is.EqualTo("CSS1Compat"));
            Assert.That(HtmlSerializer.SerializeChildren(result.Document.Body!), Is.EqualTo("<p>one</p><div>two</div>three"));
            var malformed = HtmlImporter.Import("<div>\n&bogus;</span>", "broken.html", Url);
            Assert.That(malformed.Diagnostics, Is.Not.Empty);
            foreach (var warning in malformed.Diagnostics)
            { Assert.That(warning.File, Is.EqualTo("broken.html")); Assert.That(warning.Line, Is.GreaterThan(0)); Assert.That(warning.Column, Is.GreaterThan(0)); Assert.That(warning.Severity, Is.EqualTo(WebDiagnosticSeverity.Warning)); }
        }
        [Test]
        public void ExplicitScriptingOnTokenizationDoesNotExecuteScripts()
        {
            var result = HtmlImporter.Import("<!doctype html><body><noscript><b>literal</b></noscript><script>throw Error('never executed')</script>", "noscript.html", Url, new HtmlImportOptions { IsScripting = true });
            var noscript = result.Document.GetElementsByTagName("noscript").Item(0)!;
            Assert.That(noscript.FirstChild, Is.TypeOf<Text>()); Assert.That(noscript.TextContent, Is.EqualTo("<b>literal</b>"));
            Assert.That(HtmlSerializer.SerializeOuter(noscript), Is.EqualTo("<noscript><b>literal</b></noscript>"));
        }
        [Test]
        public void ProductDefaultsToScriptingOffAndPreservesSerializationModeWhenCloned()
        {
            var document = HtmlImporter.Import("<!doctype html><body><noscript><b>literal &amp; text</b></noscript><script>throw Error('never executed')</script>", "noscript-off.html", Url).Document;
            var noscript = document.GetElementsByTagName("noscript").Item(0)!;
            Assert.That(noscript.FirstChild, Is.TypeOf<HTMLElement>());
            Assert.That(noscript.TextContent, Is.EqualTo("literal & text"));
            noscript.TextContent = "<b>&";
            Assert.That(HtmlSerializer.SerializeOuter(noscript), Is.EqualTo("<noscript>&lt;b&gt;&amp;</noscript>"));
            var clone = (Document)document.CloneNode(true);
            Assert.That(HtmlSerializer.SerializeOuter(clone.GetElementsByTagName("noscript").Item(0)!), Is.EqualTo("<noscript>&lt;b&gt;&amp;</noscript>"));
        }
        [Test]
        public void ImportKeepsForeignNamespacesAndParserOnlyAttributeNames()
        {
            var document = HtmlImporter.Import("<!doctype html><svg xlink:href='url'><foreignObject><div id=x></div></foreignObject></svg><div @odd='value'></div>", "foreign.html", Url).Document;
            var svg = document.GetElementsByTagName("svg").Item(0)!;
            Assert.That(svg.NamespaceURI, Is.EqualTo("http://www.w3.org/2000/svg")); Assert.That(svg.GetAttributeNS("http://www.w3.org/1999/xlink", "href"), Is.EqualTo("url"));
            Assert.That(document.GetElementById("x")!.NamespaceURI, Is.EqualTo("http://www.w3.org/1999/xhtml"));
            Assert.That(document.GetElementsByTagName("div").Item(1)!.GetAttribute("@odd"), Is.EqualTo("value"));
            Assert.That(HtmlSerializer.SerializeOuter(svg), Does.Contain("xlink:href=\"url\""));
        }
        [Test]
        public void DeclarativeShadowTemplatesAttachAndCanBeSerialized()
        {
            var document = HtmlImporter.Import("<!doctype html><div id=host><template shadowrootmode=open shadowrootclonable shadowrootserializable><slot></slot><b>shadow</b></template><i>light</i></div>", "shadow.html", Url).Document;
            var host = document.GetElementById("host")!;
            Assert.That(host.ChildElementCount, Is.EqualTo(1)); Assert.That(host.ShadowRoot!.ChildElementCount, Is.EqualTo(2));
            Assert.That(host.ShadowRoot.Clonable, Is.True); Assert.That(host.ShadowRoot.Serializable, Is.True);
            var serialized = HtmlSerializer.SerializeOuter(host, true); Assert.That(serialized, Does.Contain("<template shadowrootmode=\"open\""));
            Assert.That(HtmlSerializer.SerializeOuter(host), Does.Not.Contain("shadowrootmode"));
            var roundtrip = HtmlImporter.Import("<!doctype html>" + serialized, "roundtrip.html", Url).Document.GetElementById("host")!;
            Assert.That(roundtrip.ShadowRoot!.TextContent, Is.EqualTo(host.ShadowRoot.TextContent));
            var reused = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open }); Assert.That(reused.ChildNodes.Length, Is.Zero);
        }
        [Test]
        public void TemplateContentsAreInertAndSerializerEscapesByContext()
        {
            var document = HtmlImporter.Import("<!doctype html><template><div title='&quot;&amp;'>&lt;&amp;&nbsp;</div></template>", "template.html", Url).Document;
            var template = (HTMLTemplateElement)document.GetElementsByTagName("template").Item(0)!;
            Assert.That(template.ChildNodes.Length, Is.Zero); Assert.That(template.Content.ChildNodes.Length, Is.EqualTo(1)); Assert.That(template.Content.IsConnected, Is.False);
            Assert.That(HtmlSerializer.SerializeOuter(template), Is.EqualTo("<template><div title=\"&quot;&amp;\">&lt;&amp;&nbsp;</div></template>"));
            Assert.That(DomTreeDump.ToHtml5libFormat(template), Does.Contain("|   content\n|     <div>"));
        }
        [Test]
        public void ImportLimitsInputAndExpandedTree()
        {
            Assert.Throws<FormatException>(() => HtmlImporter.Import("12345", "limit.html", Url, new HtmlImportOptions { MaxInputCharacters = 4 }));
            Assert.Throws<FormatException>(() => HtmlImporter.Import("<p>x</p>", "limit.html", Url, new HtmlImportOptions { MaxNodes = 1 }));
            Assert.Throws<FormatException>(() => HtmlImporter.Import("&amp;&amp;", "limit.html", Url, new HtmlImportOptions { MaxTextCharacters = 1 }));
        }
        [Test]
        public void FactoryCoversProfileInterfaces()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            Assert.That(document.CreateElement("input"), Is.TypeOf<HTMLInputElement>());
            Assert.That(document.CreateElement("selectedcontent"), Is.TypeOf<HTMLSelectedContentElement>());
            Assert.That(document.CreateElement("dialog"), Is.TypeOf<HTMLDialogElement>());
            Assert.That(document.CreateElement("h4"), Is.TypeOf<HTMLHeadingElement>());
            Assert.That(document.CreateElement("section"), Is.TypeOf<HTMLElement>());
            Assert.That(document.CreateElement("not-a-built-in"), Is.TypeOf<HTMLElement>());
            Assert.That(document.CreateElement("nonsense"), Is.TypeOf<HTMLUnknownElement>());
        }
        [TestCase("textarea")]
        [TestCase("pre")]
        [TestCase("listing")]
        public void LeadingNewlineSurvivesSerializedElementRoundtrip(string tag)
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            var element = document.CreateElement(tag); element.Id = "newline"; element.TextContent = "\nlocalized Ω";
            var html = HtmlSerializer.SerializeOuter(element);
            Assert.That(html, Does.Contain(">\n\nlocalized Ω"));
            var imported = HtmlImporter.Import("<!doctype html>" + html, "newline.html", Url).Document;
            Assert.That(imported.GetElementById("newline")!.TextContent, Is.EqualTo(element.TextContent));
        }
    }
}
