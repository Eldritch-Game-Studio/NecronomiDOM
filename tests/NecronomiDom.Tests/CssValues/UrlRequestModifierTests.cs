using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class UrlRequestModifierTests
    {
        // CSS Values 5 §4.1.1 and WPT css/css-values/urls/url-request-modifiers-computed.sub.html.
        [TestCase("cross-origin(anonymous)")]
        [TestCase("cross-origin(use-credentials)")]
        [TestCase("integrity(\"\")")]
        [TestCase("integrity(\"sha384-foobar\")")]
        [TestCase("referrer-policy(no-referrer)")]
        [TestCase("referrer-policy(no-referrer-when-downgrade)")]
        [TestCase("referrer-policy(same-origin)")]
        [TestCase("referrer-policy(origin)")]
        [TestCase("referrer-policy(strict-origin)")]
        [TestCase("referrer-policy(origin-when-cross-origin)")]
        [TestCase("referrer-policy(strict-origin-when-cross-origin)")]
        [TestCase("referrer-policy(unsafe-url)")]
        public void EachDefinedRequestModifierSurvivesParsing(string modifier)
        {
            string text = "url(\"http://web-platform.test:8000/css/support/1x1-green.png\" " + modifier + ")";
            Assert.That(Parse(text).Serialize(), Is.EqualTo(text));
        }

        [Test]
        public void ModifiersSerializeInCanonicalOrderAndSurviveComputedUrlRebasing()
        {
            CssUrlValue specified = Parse("url(\"../asset.png\" referrer-policy(no-referrer) integrity(\"sha384-foobar\") cross-origin(anonymous))");
            Assert.That(specified.Serialize(), Is.EqualTo("url(\"../asset.png\" cross-origin(anonymous) integrity(\"sha384-foobar\") referrer-policy(no-referrer))"));
            var context = new ComputedValueContext { DocumentUrl = WebUrl.Parse("res:///ui/document.html") };
            CssValue computed = ComputedValueResolver.Resolve("background-image", specified, context);
            Assert.That(computed.Serialize(), Is.EqualTo("url(\"res:///asset.png\" cross-origin(anonymous) integrity(\"sha384-foobar\") referrer-policy(no-referrer))"));
        }

        [Test]
        public void ModifierKeywordsAreAsciiInsensitiveButIntegrityIsOpaque()
        {
            CssUrlValue value = Parse("URL(\"asset.png\" CROSS-ORIGIN(ANONYMOUS) INTEGRITY(\"Opaque\\\"Token\") REFERRER-POLICY(NO-REFERRER))");
            Assert.That(value.CrossOrigin, Is.EqualTo("anonymous"));
            Assert.That(value.ReferrerPolicy, Is.EqualTo("no-referrer"));
            Assert.That(value.Integrity, Is.EqualTo("Opaque\"Token"));
            Assert.That(value.Serialize(), Is.EqualTo("url(\"asset.png\" cross-origin(anonymous) integrity(\"Opaque\\\"Token\") referrer-policy(no-referrer))"));
        }

        [TestCase("cross-origin(other)")]
        [TestCase("cross-origin(\"anonymous\")")]
        [TestCase("integrity(anonymous)")]
        [TestCase("integrity(\"x\" \"y\")")]
        [TestCase("referrer-policy(other)")]
        [TestCase("referrer-policy(\"no-referrer\")")]
        [TestCase("referrer-policy()")]
        [TestCase("other(no-referrer)")]
        public void UnsupportedModifierGrammarDoesNotUseAPermissiveFallback(string modifier)
        {
            Assert.That(CssPropertyParser.Parse("background-image", CssParser.ParseComponentValueList("url(\"asset.png\" " + modifier + ")")), Is.Null);
        }

        private static CssUrlValue Parse(string text)
        {
            ParsedProperty? property = CssPropertyParser.Parse("background-image", CssParser.ParseComponentValueList(text));
            Assert.That(property, Is.Not.Null);
            Assert.That(property!.Longhands[0].Value, Is.TypeOf<CssUrlValue>());
            return (CssUrlValue)property.Longhands[0].Value;
        }
    }
}
