using System.Linq;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Html
{
    [TestFixture]
    public sealed class HtmlParserParityTests
    {
        private static readonly WebUrl Url = WebUrl.Parse("res:///ui/parser-parity.html")!;
        [TestCase("")]
        [TestCase(" ")]
        public void ForeignNullAndWhitespaceDoNotDisableLaterFrameset(string whitespace)
        {
            var document = HtmlImporter.Import("<svg>\0" + whitespace + "</svg><frameset>", "frameset-null.html", Url).Document;
            Assert.That(DomTreeDump.ToHtml5libFormat(document), Is.EqualTo("| <html>\n|   <head>\n|   <frameset>"));
        }
        [Test]
        public void FormattingEndTagCannotAdoptAcrossSelectScopeBoundary()
        {
            var document = HtmlImporter.Import("<font><select><option>a</option></font></select>", "select-scope.html", Url).Document;
            Assert.That(DomTreeDump.ToHtml5libFormat(document),
                Is.EqualTo("| <html>\n|   <head>\n|   <body>\n|     <font>\n|       <select>\n|         <option>\n|           \"a\""));
        }
        [Test]
        public void SelectedContentClonesAndTracksActualSelectionAndOptionChildMutations()
        {
            var document = HtmlImporter.Import("<select><button><selectedcontent></button><option><b>X</b><option><i>Y</i>", "selectedcontent.html", Url).Document;
            var select = (HTMLSelectElement)document.GetElementsByTagName("select").Single();
            var content = document.GetElementsByTagName("selectedcontent").Single();
            Assert.That(content.TextContent, Is.EqualTo("X"));
            select.SelectedIndex = 1;
            Assert.That(content.TextContent, Is.EqualTo("Y"));
            var option = (HTMLOptionElement)select.Options[1]!;
            Assert.That(content.FirstChild, Is.Not.SameAs(option.FirstChild));
            ((Text)option.FirstChild!.FirstChild!).Data = "Z";
            Assert.That(content.TextContent, Is.EqualTo("Z"));
            select.SelectedIndex = -1;
            Assert.That(content.ChildNodes.Count, Is.Zero);
        }
        [Test]
        public void SelectOptionsAreSameObjectAndExcludeNestedSelectOwnership()
        {
            var document = new Document { IsHtmlDocument = true };
            var outer = (HTMLSelectElement)document.CreateElement("select");
            var inner = (HTMLSelectElement)document.CreateElement("select");
            outer.AppendChild(inner); inner.AppendChild(document.CreateElement("option"));
            Assert.That(outer.Options, Is.SameAs(outer.Options));
            Assert.That(outer.Options.Count, Is.Zero);
            Assert.That(inner.Options.Count, Is.EqualTo(1));
        }
        [Test]
        public void FragmentInheritsAncestorFormPointerAndDocumentQuirksMode()
        {
            var document = HtmlImporter.Import("<form><div></div></form>", "context.html", Url).Document;
            var context = document.GetElementsByTagName("div").Single();
            var fragment = HtmlImporter.ImportFragment("<form><p>inside<table></table>", context, "fragment.html").Fragment;
            Assert.That(fragment.ChildNodes.OfType<HTMLFormElement>(), Is.Empty);
            var paragraph = fragment.ChildNodes.OfType<Element>().Single();
            Assert.That(paragraph.LocalName, Is.EqualTo("p"));
            Assert.That(paragraph.GetElementsByTagName("table").Count, Is.EqualTo(1));
        }
        [TestCase("<input><option>", "| <input>\n| <option>")]
        [TestCase("<keygen><option>", "| <keygen>\n| <option>")]
        [TestCase("<textarea><option>", "| <textarea>\n|   \"<option>\"")]
        public void SelectFragmentProcessesFormControlsWithoutAnOpenSelect(string html, string expected)
        {
            var document = new Document { IsHtmlDocument = true };
            var fragment = HtmlImporter.ImportFragment(html, document.CreateElement("select"), "select.html").Fragment;
            Assert.That(DomTreeDump.ToHtml5libFormat(fragment), Is.EqualTo(expected));
        }
        [Test]
        public void TemplateAfterClosedHeadUsesHeadPointer()
        {
            var document = HtmlImporter.Import("<head></head><template>", "template.html", Url).Document;
            Assert.That(DomTreeDump.ToHtml5libFormat(document), Is.EqualTo("| <html>\n|   <head>\n|     <template>\n|       content\n|   <body>"));
        }
        [Test]
        public void SelectResetAfterTemplatePreservesTableInsertionMode()
        {
            var document = HtmlImporter.Import("<body><table><tr><td><select><template>Foo</template><caption>A</table>", "table.html", Url).Document;
            var caption = document.GetElementsByTagName("caption").Single();
            Assert.That(caption.ParentNode, Is.SameAs(document.GetElementsByTagName("table").Single()));
            Assert.That(caption.TextContent, Is.EqualTo("A"));
        }
        [Test]
        public void NullCharactersAreIgnoredInHtmlAndReplacedInForeignContent()
        {
            var document = HtmlImporter.Import("<p>a\0b<math>c\0d</math>", "null.html", Url).Document;
            Assert.That(document.GetElementsByTagName("p").Single().FirstChild!.TextContent, Is.EqualTo("ab"));
            Assert.That(document.GetElementsByTagName("math").Single().TextContent, Is.EqualTo("c\uFFFDd"));
        }
        [Test]
        public void ForeignFragmentBreakoutProcessesHtmlWithoutDroppingItsNamespaceContext()
        {
            var document = new Document { IsHtmlDocument = true };
            const string svgNamespace = "http://www.w3.org/2000/svg";
            var context = document.CreateElementNS(svgNamespace, "svg");
            var fragment = HtmlImporter.ImportFragment("<p>outside</p><g>inside</g>", context, "svg.html").Fragment;
            Assert.That(((Element)fragment.ChildNodes[0]!).NamespaceURI, Is.EqualTo(Names.Html));
            Assert.That(((Element)fragment.ChildNodes[1]!).NamespaceURI, Is.EqualTo(svgNamespace));
        }
        [Test]
        public void CurrentHtmlProcessingInstructionsUseTargetAndDataWithoutExecutingAnything()
        {
            var document = HtmlImporter.Import("<?import namespace=\"foo\" implementation=\"#bar\">", "pi.html", Url).Document;
            var pi = (ProcessingInstruction)document.FirstChild!;
            Assert.That(pi.Target, Is.EqualTo("import"));
            Assert.That(pi.Data, Is.EqualTo("namespace=\"foo\" implementation=\"#bar\""));
            Assert.That(DomTreeDump.ToHtml5libFormat(HtmlImporter.Import("<?COMMENT?>", "pi.html", Url).Document),
                Is.EqualTo("| <?COMMENT >\n| <html>\n|   <head>\n|   <body>"));
        }
        [Test]
        public void IncompleteProcessingInstructionIsDiscardedButReservedXmlTargetStaysBogusComment()
        {
            var incomplete = HtmlImporter.Import("<?", "pi.html", Url).Document;
            Assert.That(incomplete.ChildNodes.OfType<ProcessingInstruction>(), Is.Empty);
            Assert.That(incomplete.ChildNodes.OfType<Comment>(), Is.Empty);
            var reserved = HtmlImporter.Import("<?xml version=\"1.0\">", "pi.html", Url).Document;
            Assert.That(((Comment)reserved.FirstChild!).Data, Is.EqualTo("?xml version=\"1.0\""));
        }
        [Test]
        public void FramesetRetainsEveryWhitespaceCharacterAcrossIgnoredText()
        {
            var document = HtmlImporter.Import("<frameset> a </frameset>", "frameset.html", Url).Document;
            Assert.That(document.GetElementsByTagName("frameset").Single().TextContent, Is.EqualTo("  "));
        }
    }
}
