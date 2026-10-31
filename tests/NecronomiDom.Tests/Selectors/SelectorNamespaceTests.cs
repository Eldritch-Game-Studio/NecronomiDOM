using System.Collections.Generic;
using NecronomiDom.Css.Selectors;
using NUnit.Framework;

namespace NecronomiDom.Tests.Selectors
{
    [TestFixture]
    public sealed class SelectorNamespaceTests
    {
        private static SelectorParseOptions Options() => new SelectorParseOptions(Namespaces: new Dictionary<string, string>
        { [""] = "urn:default", ["same"] = "urn:default", ["other"] = "urn:other" });
        [Test]
        public void ElementNamesResolveDefaultNamedNullAndWildcardNamespaces()
        {
            var list = SelectorParser.ParseSelectorList("a, other|a, |a, *|a, same|a", Options())!;
            var uris = new List<string?>();
            foreach (var selector in list.Selectors) uris.Add(((TypeSelector)selector.Compounds[0].SimpleSelectors[0]).NamespaceURI);
            Assert.That(uris, Is.EqualTo(new string?[] { "urn:default", "urn:other", "", null, "urn:default" }));
            Assert.That(list.Serialize(), Is.EqualTo("a, other|a, |a, *|a, a"));
        }
        [Test]
        public void AttributesDoNotInheritTheDefaultNamespace()
        {
            var list = SelectorParser.ParseSelectorList("[x], [other|x], [|x], [*|x]", Options())!;
            var uris = new List<string?>();
            foreach (var selector in list.Selectors) uris.Add(((AttributeSelector)selector.Compounds[0].SimpleSelectors[0]).NamespaceURI);
            Assert.That(uris, Is.EqualTo(new string?[] { "", "urn:other", "", null }));
            Assert.That(list.Serialize(), Is.EqualTo("[x], [other|x], [x], [*|x]"));
        }
        [Test]
        public void ImplicitUniversalIsNamespaceConstrainedExceptInsideMatchingPseudoSubjects()
        {
            var compound = SelectorParser.ParseSelectorList(".foo:is(.ancestor .subject, *:hover)", Options())!.Selectors[0].Compounds[0];
            Assert.That(compound.NamespaceURI, Is.EqualTo("urn:default"));
            var pseudo = (PseudoClassSelector)compound.SimpleSelectors[1];
            Assert.That(pseudo.Arguments!.Selectors[0].Compounds[0].NamespaceURI, Is.EqualTo("urn:default"));
            Assert.That(pseudo.Arguments.Selectors[0].Compounds[1].NamespaceURI, Is.Null);
            Assert.That(((UniversalSelector)pseudo.Arguments.Selectors[1].Compounds[0].SimpleSelectors[0]).NamespaceURI, Is.EqualTo("urn:default"));
        }
        [Test]
        public void ExplicitAnyNamespaceOverridesTheDefaultNamespace()
        {
            var compound = SelectorParser.ParseSelectorList("*|*.foo:not(.bar)", Options())!.Selectors[0].Compounds[0];
            Assert.That(compound.NamespaceURI, Is.Null);
            Assert.That(((UniversalSelector)compound.SimpleSelectors[0]).NamespaceURI, Is.Null);
            Assert.That(((PseudoClassSelector)compound.SimpleSelectors[2]).Arguments!.Selectors[0].Compounds[0].NamespaceURI, Is.Null);
        }
    }
}
