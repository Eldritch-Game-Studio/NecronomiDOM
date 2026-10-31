using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class DescriptorTests
    {
        [TestCase("font-face", "font-display", "swap", true)]
        [TestCase("font-face", "font-display", "inherit", false)]
        [TestCase("font-face", "font-family", "My Font", true)]
        [TestCase("font-face", "font-family", "serif", false)]
        [TestCase("font-face", "font-family", "'serif'", true)]
        [TestCase("font-face", "font-weight", "100 900", true)]
        [TestCase("font-face", "font-weight", "900 100", false)]
        [TestCase("font-face", "font-style", "oblique -45deg 45deg", true)]
        [TestCase("font-face", "font-style", "oblique 1turn", false)]
        [TestCase("font-face", "size-adjust", "150%", true)]
        [TestCase("font-face", "size-adjust", "-1%", false)]
        [TestCase("font-face", "src", "url('font.woff2') format('woff2') tech(variations), local('Test Font')", true)]
        [TestCase("font-face", "unicode-range", "U+0020-007F, U+4??", true)]
        [TestCase("font-face", "unicode-range", "U+110000", false)]
        [TestCase("font-face", "unicode-range", "U+FFFF-0020", false)]
        [TestCase("counter-style", "system", "extends decimal", true)]
        [TestCase("counter-style", "system", "extends none", false)]
        [TestCase("counter-style", "fallback", "none", false)]
        [TestCase("counter-style", "symbols", "'a' 'b'", true)]
        [TestCase("counter-style", "range", "1 10, 20 infinite", true)]
        [TestCase("counter-style", "range", "10 1", false)]
        [TestCase("counter-style", "additive-symbols", "100 'C', 10 'X', 1 'I'", true)]
        [TestCase("counter-style", "additive-symbols", "10 'X', 100 'C'", false)]
        [TestCase("counter-style", "additive-symbols", "10 'X', 10 'Y'", false)]
        [TestCase("counter-style", "pad", "3 '0'", true)]
        [TestCase("counter-style", "pad", "-1 '0'", false)]
        [TestCase("property", "syntax", "'<length> | auto'", true)]
        [TestCase("property", "syntax", "'<length> && auto'", false)]
        [TestCase("property", "syntax", "'<unknown>'", false)]
        [TestCase("property", "inherits", "true", true)]
        [TestCase("property", "inherits", "yes", false)]
        [TestCase("position-try", "top", "anchor(bottom)", true)]
        [TestCase("position-try", "width", "10px", true)]
        [TestCase("position-try", "color", "red", false)]
        [TestCase("position-try", "margin-trim", "block", false)]
        [TestCase("property", "initial-value", "1px; color: red", false)]
        [TestCase("property", "initial-value", "1px !important", false)]
        [TestCase("styleset", "my-set", "1 2 3", true)]
        [TestCase("character-variant", "my-set", "1 2 3", false)]
        [TestCase("styleset", "my-set", "calc(1 + 1) 3", true)]
        [TestCase("stylistic", "my-set", "calc(1 + 1)", true)]
        [TestCase("stylistic", "my-set", "1.5", false)]
        [TestCase("stylistic", "my-set", "-1", false)]
        [TestCase("stylistic", "default", "1", false)]
        [TestCase("counter-style", "additive-symbols", "calc(10) 'X', calc(1) 'I'", true)]
        [TestCase("counter-style", "additive-symbols", "calc(1) 'I', calc(10) 'X'", false)]
        [TestCase("counter-style", "range", "calc(10) calc(1)", false)]
        [TestCase("font-face", "font-weight", "calc(900) calc(100)", false)]
        [TestCase("font-face", "font-weight", "calc(-1) calc(-100)", true)]
        [TestCase("font-face", "font-width", "calc(-1%) calc(-100%)", true)]
        [TestCase("does-not-exist", "anything", "anything", false)]
        public void ScopedDescriptorGrammar(string rule, string descriptor, string input, bool expected)
            => Assert.That(CssDescriptorParser.TryParse(rule, descriptor, CssParser.ParseComponentValueList(input), out _), Is.EqualTo(expected));

        [TestCase("*", "anything 1px 'raw'", true)]
        [TestCase("*", "anything; else", false)]
        [TestCase("*", "anything !important", false)]
        [TestCase("<length> | auto", "auto", true)]
        [TestCase("<length> | auto", "1px", true)]
        [TestCase("<length> | auto", "1", false)]
        [TestCase("<number>+", "1 2 3", true)]
        [TestCase("<number>#", "1, 2, 3", true)]
        [TestCase("<number>#", "1 2 3", false)]
        [TestCase("<transform-list>", "translateX(1px) rotate(30deg)", true)]
        [TestCase("<length> && <number>", "1px 1", false)]
        [TestCase("<transform-list>+", "rotate(1deg)", false)]
        [TestCase("*", "steps(-1) whatever(also arbitrary)", true)]
        // Chrome 155 registers the quoted <string> initial value (computed "seed") and rejects the bare ident.
        [TestCase("<string>", "\"seed\"", true)]
        [TestCase("<string>", "seed", false)]
        [TestCase("<string>+", "\"one\" \"two\"", true)]
        [TestCase("<integer>", "10000000", true)]
        [TestCase("\\66 oo | <number>", "FOO", true)]
        [TestCase("\\|", "\\|", true)]
        [TestCase("\\+", "\\+", true)]
        [TestCase("<number> |", "1", false)]
        [TestCase("<number>/**/+", "1", false)]
        public void RegistrationInitialValueSyntax(string syntax, string input, bool expected)
            => Assert.That(CssRegistrationSyntax.TryParseValue(syntax, CssParser.ParseComponentValueList(input), out _), Is.EqualTo(expected));

        [TestCase("<string>", "\"attribute text\"", true)]
        [TestCase("<string>+", "\"one\" \"two\"", true)]
        [TestCase("<url>", "url(\"font.woff2\")", false)]
        [TestCase("<number> | <url>", "1", false)]
        [TestCase("<transform-list>+", "rotate(1deg)", false)]
        public void TypedAttributeSyntaxHasConsumerSpecificTypes(string syntax, string input, bool expected)
            => Assert.That(CssRegistrationSyntax.TryParseValue(syntax, CssParser.ParseComponentValueList(input), out _,
                CssSyntaxDefinitionContext.Attribute), Is.EqualTo(expected));

        [TestCase("width", "AUTO", "auto")]
        [TestCase("margin", "1px 2px 1px 2px", "1px 2px")]
        [TestCase("margin", "INHERIT", "inherit")]
        [TestCase("margin", "var(--spacing)", "var(--spacing)")]
        public void PositionTryDescriptorUsesSpecifiedPropertySerialization(string descriptor, string input, string expected)
        {
            Assert.That(CssDescriptorParser.TryParse("position-try", descriptor, CssParser.ParseComponentValueList(input), out CssValue? value), Is.True);
            Assert.That(value!.Serialize(), Is.EqualTo(expected));
        }

        [Test]
        public void RegistrationInitialDescriptorPreservesItsUnparsedTokenStream()
        {
            Assert.That(CssDescriptorParser.TryParse("property", "initial-value",
                CssParser.ParseComponentValueList("CaseSensitive 1px"), out CssValue? value), Is.True);
            Assert.That(value, Is.TypeOf<CssUnparsedValue>());
            Assert.That(value!.Serialize(), Is.EqualTo("CaseSensitive 1px"));
        }
    }
}
