using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    [TestFixture]
    public sealed class StyleEngineTests
    {
        [Test]
        public void LayersInlineImportantAndLiveMutationsUseOneComputedView()
        {
            var document = Document("@layer first, second; @layer first { div { opacity:.2 !important; width:10px; } } @layer second { div { opacity:.4 !important; width:20px; } } div { width:30px; }");
            var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element);
            var computed = document.DefaultView.GetComputedStyle(element);
            Assert.That(computed.GetPropertyValue("width"), Is.EqualTo("30px"));
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.2"));
            element.Style.SetProperty("width", "40px");
            Assert.That(document.DefaultView.GetComputedStyle(element), Is.SameAs(computed));
            Assert.That(computed.GetPropertyValue("width"), Is.EqualTo("40px"));
            Assert.That(computed.CssText, Is.Empty);
            Assert.Throws<DomException>(() => computed.SetProperty("width", "50px"));
        }
        [Test]
        public void StyleBlockCollectionTracksTreeOrderAndTextChanges()
        {
            var document = Document("div { opacity:.2; }");
            var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element);
            var second = (HTMLStyleElement)document.CreateElement("style"); second.TextContent = "div { opacity:.4; }"; document.Head!.AppendChild(second);
            var computed = document.DefaultView.GetComputedStyle(element);
            Assert.That(document.StyleSheets.Count, Is.EqualTo(2));
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.4"));
            document.Head.InsertBefore(second, document.Head.FirstChild);
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.2"));
            second.TextContent = "div { opacity:.8 !important; }";
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.8"));
            second.Remove();
            Assert.That(document.StyleSheets.Count, Is.EqualTo(1));
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.2"));
        }
        [Test]
        public void ConstructedSheetMutationInvalidatesAdoptedStyles()
        {
            var document = Document("div { opacity:.2; }");
            var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element);
            var sheet = new CSSStyleSheet(); sheet.ReplaceSync("div { opacity:.7; }"); document.AdoptedStyleSheets.Add(sheet);
            var computed = document.DefaultView.GetComputedStyle(element);
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.7"));
            sheet.ReplaceSync("div { opacity:.9; }");
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.9"));
            sheet.Disabled = true;
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo("0.2"));
        }
        [Test]
        public void ShadowScopesDoNotLeakAndHostAndSlotSelectorsWork()
        {
            var document = Document("span { opacity:.2; }");
            var host = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(host);
            var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
            var sheet = new CSSStyleSheet(); sheet.ReplaceSync(":host { width:25px; } span { opacity:.4; } ::slotted(.assigned) { opacity:.6; }"); shadow.AdoptedStyleSheets.Add(sheet);
            var inside = (HTMLElement)document.CreateElement("span"); shadow.AppendChild(inside);
            var slot = document.CreateElement("slot"); shadow.AppendChild(slot);
            var assigned = (HTMLElement)document.CreateElement("b"); assigned.ClassName = "assigned"; host.AppendChild(assigned);
            Assert.That(document.DefaultView.GetComputedStyle(host).GetPropertyValue("width"), Is.EqualTo("25px"));
            Assert.That(document.DefaultView.GetComputedStyle(inside).GetPropertyValue("opacity"), Is.EqualTo("0.4"));
            Assert.That(document.DefaultView.GetComputedStyle(assigned).GetPropertyValue("opacity"), Is.EqualTo("0.6"));
        }
        [Test]
        public void RegisteredInvalidCustomValueFallsBackBeforeVariableSubstitution()
        {
            var document = Document("@property --length { syntax:'<length>'; inherits:false; initial-value:3px; } div { --length:red; width:var(--length); }");
            var element = document.CreateElement("div"); document.Body!.AppendChild(element);
            Assert.That(document.DefaultView.GetComputedStyle(element).GetPropertyValue("width"), Is.EqualTo("3px"));
        }
        [Test]
        public void TypedAttributeSubstitutesActualVariablesBeforeItsTypeValidation()
        {
            // Matches Chrome 155 computed style.
            var document = Document("div { --length:5px; width:attr(data-length type(<length>),10px); }");
            var element = document.CreateElement("div"); element.SetAttribute("data-length", "var(--length)");
            document.Body!.AppendChild(element);
            Assert.That(document.DefaultView.GetComputedStyle(element).GetPropertyValue("width"), Is.EqualTo("5px"));
        }
        [Test]
        public void ComputedFontShorthandSerializesCanonicalWidthAndRoundTrips()
        {
            // WPT font-computed.html requires nonempty CSSOM font and semantic round-trip.
            var document = Document("div { font:italic 16px serif; }");
            var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element);
            string font = document.DefaultView.GetComputedStyle(element).GetPropertyValue("font");
            // Matches Chrome 155 computed style.
            Assert.That(font, Is.EqualTo("italic 16px serif"));
            Assert.That(font, Is.Not.Empty);
            element.Style.SetProperty("font", font);
            Assert.That(element.Style.GetPropertyValue("font"), Is.Not.Empty);
            Assert.That(document.DefaultView.GetComputedStyle(element).GetPropertyValue("font"), Is.EqualTo(font));
        }
        [Test]
        public void NamespaceSelectorsDistinguishForeignElementsAndAnyNamespace()
        {
            var document = Document("@namespace n 'urn:test'; n|item { opacity:.2; } *|item { width:12px; }");
            var foreign = document.CreateElementNS("urn:test", "item"); var html = document.CreateElement("item");
            document.Body!.AppendChild(foreign); document.Body.AppendChild(html);
            Assert.That(document.DefaultView.GetComputedStyle(foreign).GetPropertyValue("opacity"), Is.EqualTo("0.2"));
            Assert.That(document.DefaultView.GetComputedStyle(html).GetPropertyValue("opacity"), Is.EqualTo("1"));
            Assert.That(document.DefaultView.GetComputedStyle(html).GetPropertyValue("width"), Is.EqualTo("12px"));
        }
        [TestCase("", "1px", "6px")]
        [TestCase("button { appearance:none; }", "1px", "6px")]
        [TestCase("button { padding:0; }", "0px", "0px")]
        [TestCase("button { padding-block:3px; padding-inline:4px; }", "3px", "4px")]
        public void ButtonUaPaddingMatchesChromeAndRemainsAuthorOverridable(string css, string block, string inline)
        {
            var document = Document(css);
            var button = (HTMLElement)document.CreateElement("button"); document.Body!.AppendChild(button);
            var computed = document.DefaultView.GetComputedStyle(button);
            Assert.That(computed.GetPropertyValue("padding-top"), Is.EqualTo(block));
            Assert.That(computed.GetPropertyValue("padding-bottom"), Is.EqualTo(block));
            Assert.That(computed.GetPropertyValue("padding-left"), Is.EqualTo(inline));
            Assert.That(computed.GetPropertyValue("padding-right"), Is.EqualTo(inline));
            button.Style.SetProperty("padding", "2px 8px");
            Assert.That(computed.GetPropertyValue("padding-top"), Is.EqualTo("2px"));
            Assert.That(computed.GetPropertyValue("padding-left"), Is.EqualTo("8px"));
        }
        private static Document Document(string css)
        {
            var document = new NecronomiDom.Dom.Document().Implementation.CreateHTMLDocument("cascade");
            var style = (HTMLStyleElement)document.CreateElement("style"); style.TextContent = css; document.Head!.AppendChild(style);
            return document;
        }
    }
}
