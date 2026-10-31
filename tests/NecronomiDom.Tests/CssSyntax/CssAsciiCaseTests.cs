using NecronomiDom.Css.Media;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssSyntax
{
    [TestFixture]
    public sealed class CssAsciiCaseTests
    {
        [TestCase("K", "k", true)]
        [TestCase("S", "s", true)]
        [TestCase("\u212A", "k", false)]
        [TestCase("\u017F", "s", false)]
        [TestCase("\u0131", "i", false)]
        public void IdentMatchingOnlyFoldsAsciiLetters(string source, string keyword, bool expected)
            => Assert.That(new CssToken(CssTokenKind.Ident, source).IsIdent(keyword), Is.EqualTo(expected));

        [Test]
        public void PropertyNamesDoNotFoldUnicodeIntoSupportedNames()
        {
            Assert.That(CssParser.ParseDeclaration("Padding-bloc\u212A-START:1px")!.Name, Is.EqualTo("padding-bloc\u212A-start"));
            var style = CSSStyleDeclaration.Parse("padding-block-start: 1px");
            style.SetProperty("padding-bloc\u212A-start", "2px");
            Assert.That(style.GetPropertyValue("padding-block-start"), Is.EqualTo("1px"));
            Assert.That(style.GetPropertyValue("padding-bloc\u212A-start"), Is.Empty);
        }
        [TestCase(":chec\u212Aed")]
        [TestCase(":\u017Fcope")]
        [TestCase("[a=b \u017F]")]
        public void PseudoAndAttributeFlagsCannotUseUnicodeCaseEquivalents(string source)
            => Assert.That(SelectorParser.ParseSelectorList(source), Is.Null);

        [Test]
        public void MediaNamesPreserveNonAsciiCodePoints()
        {
            Assert.That(MediaQueryParser.Parse("SCREEN").Serialize(), Is.EqualTo("screen"));
            Assert.That(MediaQueryParser.Parse("\u017Fcreen").Serialize(), Is.EqualTo("\u017Fcreen"));
            Assert.That(MediaQueryParser.Parse("(prefers-color-scheme: DAR\u212A)").Serialize(), Is.EqualTo("(prefers-color-scheme: dar\u212A)"));
        }
        [Test]
        public void CounterNamesOnlyCanonicalizeAsciiPredefinedNames()
        {
            var rule = (CSSCounterStyleRule)CSSStyleSheet.Parse("@counter-style \u212Ahmer {symbols:'x'}").CssRules[0];
            Assert.That(rule.Name, Is.EqualTo("\u212Ahmer"));
            rule.Name = "KHMER";
            Assert.That(rule.Name, Is.EqualTo("khmer"));
        }
    }
}
