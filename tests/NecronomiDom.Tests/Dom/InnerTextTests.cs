using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    public sealed class InnerTextTests
    {
        [Test]
        public void SetterNormalizesNewlinesIntoRealBreakElementsAndReplacesChildren()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            var element = (HTMLElement)document.CreateElement("div");
            element.Append("old");
            element.InnerText = "A\r\nB\rC\n\n";
            Assert.That(element.ChildNodes.Length, Is.EqualTo(7));
            Assert.That(element.GetElementsByTagName("br").Length, Is.EqualTo(4));
            Assert.That(element.TextContent, Is.EqualTo("ABC"));
            Assert.That(element.InnerText, Is.EqualTo("ABC")); // No render tree: HTML's textContent branch.
            element.InnerText = string.Empty;
            Assert.That(element.ChildNodes.Length, Is.Zero);
        }
        [Test]
        public void GetterUsesActualHostRenderedTextWithoutCreatingASecondTextLayout()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            var element = (HTMLElement)document.CreateElement("div"); element.TextContent = "raw";
            document.DefaultView.RenderedText = item => ReferenceEquals(item, element) ? "rendered\ntext" : null;
            Assert.That(element.InnerText, Is.EqualTo("rendered\ntext"));
        }
        [Test]
        public void StyleInnerTextSetterFeedsTheRealKeyframesParser()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            var style = (HTMLStyleElement)document.CreateElement("style");
            style.InnerText = "@keyframes foo {}"; document.Head!.Append(style);
            Assert.That(style.Sheet!.CssRules.Count, Is.EqualTo(1));
        }
    }
}
