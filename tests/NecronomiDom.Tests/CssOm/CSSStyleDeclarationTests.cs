using System.Linq;
using NecronomiDom.Css;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssOm
{
    [TestFixture]
    public sealed class CSSStyleDeclarationTests
    {
        [Test]
        public void InvalidDeclarationsDropWithoutLosingLaterProperties()
        {
            var style = CSSStyleDeclaration.Parse("color: red; width: nonsense; nope: 3; height: 5px; junk; opacity: .5");
            Assert.That(style.GetPropertyValue("color"), Is.EqualTo("red"));
            Assert.That(style.GetPropertyValue("width"), Is.Empty);
            Assert.That(style.GetPropertyValue("height"), Is.EqualTo("5px"));
            Assert.That(style.GetPropertyValue("opacity"), Is.EqualTo("0.5"));
        }
        [Test]
        public void ParsingImportanceAndSetPropertyHaveDifferentReplacementSemantics()
        {
            var style = CSSStyleDeclaration.Parse("color: red !important; color: blue; color: green !important");
            Assert.That(style.GetPropertyValue("color"), Is.EqualTo("green"));
            Assert.That(style.GetPropertyPriority("COLOR"), Is.EqualTo("important"));
            style.SetProperty("color", "black");
            Assert.That(style.GetPropertyValue("color"), Is.EqualTo("black"));
            Assert.That(style.GetPropertyPriority("color"), Is.Empty);
            style.SetProperty("color", "white", "bad");
            Assert.That(style.GetPropertyValue("color"), Is.EqualTo("black"));
            style.SetProperty("color", "white !important");
            Assert.That(style.GetPropertyValue("color"), Is.EqualTo("black"));
        }
        [Test]
        public void ShorthandsExpandSerializeAndRemoveAsAUnit()
        {
            var style = CSSStyleDeclaration.Parse("margin:1px 2px !important;");
            Assert.That(style.Length, Is.EqualTo(4));
            Assert.That(style.GetPropertyValue("margin-left"), Is.EqualTo("2px"));
            Assert.That(style.GetPropertyValue("margin"), Is.EqualTo("1px 2px"));
            Assert.That(style.CssText, Is.EqualTo("margin: 1px 2px !important;"));
            Assert.That(style.RemoveProperty("margin"), Is.EqualTo("1px 2px"));
            Assert.That(style.Length, Is.Zero);
        }

        [TestCase("font", "caption")]
        [TestCase("font", "xx-small serif")]
        [TestCase("font", "inherit")]
        [TestCase("font", "var(--theme-font)")]
        [TestCase("border", "1px solid red")]
        public void NestedAndAliasedShorthandLeavesSupportCssomApis(string property, string value)
        {
            var style = CSSStyleDeclaration.Parse(property + ":" + value + "!important");
            Assert.That(style.Length, Is.GreaterThan(0));
            Assert.That(style.GetPropertyValue(property), Is.EqualTo(value));
            Assert.That(style.GetPropertyPriority(property), Is.EqualTo("important"));
            Assert.That(style.CssText, Is.EqualTo(property + ": " + value + " !important;"));
            Assert.That(style.RemoveProperty(property), Is.EqualTo(value));
            Assert.That(style.Length, Is.Zero);
        }

        [Test]
        public void SystemFontRetainsEmptyLeafValuesForRootSerializationAndTypedRemoval()
        {
            var style = CSSStyleDeclaration.Parse("font: caption");
            Assert.That(style.EnumerateLonghands().Any(d => d.Name == "font-width"), Is.True);
            Assert.That(style.EnumerateLonghands().Any(d => d.Name is "font-stretch" or "font-variant"), Is.False);
            Assert.That(style.GetPropertyValue("font-size"), Is.Empty);
            Assert.That(style.GetPropertyValue("font"), Is.EqualTo("caption"));
            var beforeChanges = 0;
            var changes = 0;
            style.BeforeChange = () => beforeChanges++;
            style.Changed = () => changes++;
            style.RemoveTypedProperty("font");
            Assert.That(style.Length, Is.Zero);
            Assert.That(beforeChanges, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
            style.RemoveTypedProperty("font");
            Assert.That(beforeChanges, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
        }

        [TestCase("caption", "menu")]
        [TestCase("var(--old-font)", "var(--new-font)")]
        [TestCase("caption", "var(--new-font)")]
        [TestCase("var(--old-font)", "caption")]
        public void EmptySerializingFontLeavesStillTrackTheirSpecifiedSource(string original, string replacement)
        {
            var style = CSSStyleDeclaration.Parse("font:" + original);
            var beforeChanges = 0;
            var changes = 0;
            style.BeforeChange = () => beforeChanges++;
            style.Changed = () => changes++;
            style.SetProperty("font", replacement);
            Assert.That(style.GetPropertyValue("font"), Is.EqualTo(replacement));
            Assert.That(style.CssText, Is.EqualTo("font: " + replacement + ";"));
            Assert.That(beforeChanges, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
            style.SetProperty("font", replacement);
            Assert.That(beforeChanges, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
        }
        [Test]
        public void MixedPrioritiesCannotSerializeAsAShorthand()
        {
            var style = CSSStyleDeclaration.Parse("margin: 1px; margin-left: 2px !important");
            Assert.That(style.GetPropertyValue("margin"), Is.Empty);
            Assert.That(style.GetPropertyPriority("margin"), Is.Empty);
            Assert.That(style.CssText, Does.Contain("margin-left: 2px !important;"));
        }
        [Test]
        public void CustomPropertiesKeepCaseAndBalancedTokenSequences()
        {
            var style = CSSStyleDeclaration.Parse("--Mixed: {a:b}; --mixed: var(--Mixed, x); color: var(--Mixed)");
            Assert.That(style.GetPropertyValue("--Mixed"), Is.EqualTo("{a:b}"));
            Assert.That(style.GetPropertyValue("--mixed"), Does.StartWith("var("));
            Assert.That(style.EnumerateLonghands().Select(d => d.Name), Is.EqualTo(new[] { "--Mixed", "--mixed", "color" }));
            Assert.That(style.RemoveProperty("--MIXED"), Is.Empty);
        }
        [Test]
        public void CssTextReplacesRatherThanMergesAndChangesAreObservable()
        {
            var style = CSSStyleDeclaration.Parse("width: 1px");
            var changes = 0;
            style.Changed = () => changes++;
            style.CssText = "height: 2px";
            Assert.That(style.GetPropertyValue("width"), Is.Empty);
            Assert.That(style.Item(-1), Is.Empty);
            Assert.That(style.Item(1), Is.Empty);
            Assert.That(style.Item(0), Is.EqualTo("height"));
            Assert.That(changes, Is.EqualTo(1));
            style.SetProperty("height", "");
            Assert.That(style.Length, Is.Zero);
        }
        [Test]
        public void ReadonlyComputedDeclarationCanRefreshLazilyWithoutMutationCallback()
        {
            var source = CSSStyleDeclaration.Parse("width: 1px");
            var style = new CSSStyleDeclaration { ReadOnly = true };
            var reads = 0;
            style.BeforeRead = () => { reads++; style.ReplaceComputed(source.EnumerateLonghands()); };
            Assert.That(style.GetPropertyValue("width"), Is.EqualTo("1px"));
            Assert.That(reads, Is.EqualTo(1));
            Assert.Throws<DomException>(() => style.SetProperty("width", "2px"));
            Assert.Throws<DomException>(() => style.CssText = "height: 1px");
        }

        [TestCase(false, "width: 10px;")]
        [TestCase(true, "")]
        public void CssTextUsesComputedFlagRatherThanReadonlyFlag(bool computed, string expected)
        {
            var style = CSSStyleDeclaration.Parse("width: 10px");
            style.ReadOnly = true;
            style.IsComputed = computed;
            var reads = 0;
            style.BeforeRead = () => reads++;
            Assert.That(style.CssText, Is.EqualTo(expected));
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(style.GetPropertyValue("width"), Is.EqualTo("10px"));
            Assert.That(style.GetPropertyPriority("width"), Is.Empty);
            Assert.Throws<DomException>(() => style.CssText = "");
        }
        [TestCase("#0", "\\#0")]
        [TestCase("0a", "\\30 a")]
        [TestCase("-", "\\-")]
        [TestCase("a\0b", "a\uFFFDb")]
        public void CssEscapeUsesIdentifierSerialization(string source, string expected) => Assert.That(CssStatics.Escape(source), Is.EqualTo(expected));
        [Test]
        public void SupportsUsesActualPropertyAndSelectorGrammars()
        {
            Assert.That(CssStatics.Supports("display", "grid"), Is.True);
            Assert.That(CssStatics.Supports("display", "nonsense"), Is.False);
            Assert.That(CssStatics.Supports("color", "red !important"), Is.False);
            Assert.That(CssStatics.Supports("selector(.a > .b) and (display: grid)"), Is.True);
            Assert.That(CssStatics.Supports("selector(:unknown)"), Is.False);
            Assert.That(CssStatics.Supports("future(whatever)"), Is.False);
            Assert.That(CssStatics.Supports("display: grid"), Is.True, "CSS.supports API also accepts a bare declaration");
            Assert.That(CssStatics.Supports("container-type:size"), Is.True, "WPT cq-testcommon.js precondition");
            Assert.That(CssStatics.Supports("display: nonsense"), Is.False);
        }

        [Test]
        public void PendingAttributeAndCustomStringsKeepSourceQuotesButTypedStringsStayCanonical()
        {
            // Chrome 155 keeps the specified form of content: attr(data-label type(<string>), 'fallback').
            const string value = "attr(data-label type(<string>), 'fallback')";
            var style = CSSStyleDeclaration.Parse("content: " + value + "; --raw: '\\66 allback'");
            Assert.That(style.GetPropertyValue("content"), Is.EqualTo(value));
            Assert.That(style.CssText, Does.StartWith("content: " + value + ";"));
            Assert.That(style.GetPropertyValue("--raw"), Is.EqualTo("'\\66 allback'"));
            style.SetProperty("content", "'fallback'");
            Assert.That(style.GetPropertyValue("content"), Is.EqualTo("\"fallback\""));
        }
    }
}
