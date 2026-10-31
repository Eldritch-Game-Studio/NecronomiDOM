using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Selectors;
using NUnit.Framework;

namespace NecronomiDom.Tests.Selectors
{
    [TestFixture]
    public sealed class SelectorContextTests
    {
        [TestCase(":is(.a,:unknown)", ":is(.a)", true)]
        [TestCase(":where(:unknown)", ":where()", true)]
        [TestCase(":is(,,,)", ":is()", true)]
        [TestCase(":has(:is(:has(*)))", ":has(:is())", true)]
        [TestCase(":host(:is(.a,.b+.c,.d))", ":host(:is(.a, .d))", true)]
        [TestCase("::part(foo):is([x='value'])", "::part(foo):is()", true)]
        [TestCase(":host(:is(.a))", ":host(:is(.a))", false)]
        [TestCase("::part(foo):is(:hover)", "::part(foo):is(:hover)", false)]
        public void ForgivingContextsKeepSupportMetadataWithoutSerializingDiscardedBranches(string source, string expected, bool invalid)
        {
            var list = SelectorParser.ParseSelectorList(source)!;
            Assert.That(list, Is.Not.Null);
            Assert.That(list.Serialize(), Is.EqualTo(expected));
            Assert.That(list.HasInvalidSelectors, Is.EqualTo(invalid));
        }
        [TestCase(":host(:not(.a .b))")]
        [TestCase("::part(foo):not(ul)")]
        [TestCase(":is(.a,url(a b))")]
        [TestCase(":is(.a,\"a\nb\")")]
        [TestCase(":is(# x[)")]
        public void UnforgivingAndAnyValueFailuresRejectTheSelector(string source)
            => Assert.That(SelectorParser.ParseSelectorList(source), Is.Null);

        [TestCase(":state(\\31 x)", "1x", ":state(\\31 x)")]
        [TestCase(":state(foo\\00003f bar)", "foo?bar", ":state(foo\\?bar)")]
        [TestCase(":STATE(--Camel)", "--Camel", ":state(--Camel)")]
        public void StateArgumentsKeepDecodedIdentifiersSeparateFromCssSerialization(string source, string decoded, string serialized)
        {
            var selector = SelectorParser.ParseSelectorList(source)!;
            var state = (PseudoClassSelector)selector.Selectors[0].Compounds[0].SimpleSelectors[0];
            Assert.That(state.DecodedArgument, Is.EqualTo(decoded));
            Assert.That(selector.Serialize(), Is.EqualTo(serialized));
        }

        [TestCase(":state(--foo)")]
        [TestCase(":state(bar)")]
        [TestCase(":state(--)")]
        [TestCase(":host(:state(--foo))")]
        [TestCase("::part(inner):state(bar)::before")]
        [TestCase("::part(foo)::file-selector-button")]
        public void DefinedStateAndShadowSelectorsHaveExplicitGrammar(string source)
            => Assert.That(SelectorParser.ParseSelectorList(source)?.Serialize(), Is.EqualTo(source));

        [TestCase(":state")]
        [TestCase(":state()")]
        [TestCase(":state(0)")]
        [TestCase(":state(foo bar)")]
        [TestCase("::before:state(foo)")]
        [TestCase("::slotted(foo):state(foo)")]
        [TestCase(":has-slotted(*)")]
        [TestCase(":has-slotted")]
        public void StateAndSlotGrammarRejectsWrongForms(string source)
            => Assert.That(SelectorParser.ParseSelectorList(source), Is.Null);

        [Test]
        public void ExplicitUniversalDefaultNamespaceIsNotLostInsideLogicalSubjectSelectors()
        {
            var options = new SelectorParseOptions(Namespaces: new Dictionary<string, string> { [""] = "urn:default" });
            Assert.That(SelectorParser.ParseSelectorList(":is(*.a)", options)!.Serialize(), Is.EqualTo(":is(*.a)"));
            Assert.That(SelectorParser.ParseSelectorList("*.a", options)!.Serialize(), Is.EqualTo(".a"));
            Assert.That(SelectorParser.ParseSelectorList("*|*.a", options)!.Serialize(), Is.EqualTo("*|*.a"));
        }
        [Test]
        public void InvalidForgivingNestingKeepsSourceSpellingAndDoesNotInventImplicitParents()
        {
            // Chrome 155 keeps the invalid forgiving branch spelling and adds no implicit parent.
            const string text = ":is(&:unknown([x='value']), .ok)";
            var style = (CSSStyleRule)CSSStyleSheet.Parse("a { " + text + " {} }").CssRules[0];
            Assert.That(((CSSStyleRule)style.CssRules[0]).SelectorText, Is.EqualTo(text));
            var ordinary = (CSSStyleRule)CSSStyleSheet.Parse("a { :is(:unknown, .ok) {} }").CssRules[0];
            Assert.That(((CSSStyleRule)ordinary.CssRules[0]).SelectorText, Is.EqualTo("& :is(.ok)"));
        }
    }
}
