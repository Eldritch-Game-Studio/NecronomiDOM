using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class PropertyParsingTests
    {
        // WPT cd1d8aa5bfebc15d1af2af230c7ff95f9e582a4c:
        // css/css-fonts/parsing/font-feature-settings-valid.html lines 17–21.
        [TestCase("font-feature-settings", "\"dlig\" 1", "\"dlig\"")]
        [TestCase("font-feature-settings", "\"smcp\" on", "\"smcp\"")]
        [TestCase("font-feature-settings", "\"liga\" off", "\"liga\" 0")]
        [TestCase("font-feature-settings", "'c2sc'", "\"c2sc\"")]
        [TestCase("font-feature-settings", "\"tnum\", 'hist'", "\"tnum\", \"hist\"")]
        [TestCase("display", "BLOCK", "block")]
        [TestCase("width", "0", "0px")]
        [TestCase("width", "10EM", "10em")]
        [TestCase("opacity", "0.123456789", "0.123457")]
        [TestCase("font-family", "MyFont, serif", "MyFont, serif")]
        [TestCase("font-family", "\"New Century Schoolbook\", serif", "New Century Schoolbook, serif")]
        [TestCase("font-family", "'21st Century', fantasy", "\"21st Century\", fantasy")]
        [TestCase("opacity", "calc(25% + 0.25)", "calc(0.5)")]
        [TestCase("grid-template-rows", "minmax(10px, 1fr)", "minmax(10px, 1fr)")]
        [TestCase("top", "anchor(--target bottom, 1px)", "anchor(--target bottom, 1px)")]
        [TestCase("width", "anchor-size(--target width)", "anchor-size(--target width)")]
        [TestCase("transform", "ROTATEX(0)", "rotateX(0deg)")]
        [TestCase("border-spacing", "2px 2px", "2px")]
        [TestCase("border-top-left-radius", "2px 2px", "2px")]
        [TestCase("border-bottom-right-radius", "1px 20%", "1px 20%")]
        [TestCase("-webkit-border-top-right-radius", "3px", "3px")]
        [TestCase("animation-trigger", "--menu play", "--menu play")]
        [TestCase("border-shape", "none", "none")]
        [TestCase("text-decoration-inset", "auto", "auto")]
        public void SpecifiedSerialization(string property, string input, string expected)
        {
            ParsedProperty? parsed = Parse(property, input);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed!.Longhands.Single().Value.Serialize(), Is.EqualTo(expected));
        }

        [TestCase("width", "10garbage")]
        [TestCase("width", "-1px")]
        [TestCase("padding", "-1px")]
        [TestCase("display", "bogus")]
        [TestCase("opacity", "rgb(1 2 3)")]
        [TestCase("z-index", "1.5")]
        [TestCase("font-feature-settings", "\"abc\" on")]
        [TestCase("font-variation-settings", "\"abcde\" 1")]
        [TestCase("grid-template-areas", "\"a a\" \"a b\"")]
        [TestCase("grid-template-areas", "\"a a\" \"b\"")]
        [TestCase("grid-template-columns", "minmax(1fr, 2fr)")]
        [TestCase("animation", "-1s foo")]
        [TestCase("transition", "-1s opacity")]
        [TestCase("transition", "none, opacity")]
        [TestCase("transition-timing-function", "steps(1, jump-none)")]
        [TestCase("transition-timing-function", "linear(1)")]
        [TestCase("clip-path", "path(\" \" )")]
        [TestCase("clip-path", "path(\"L0 0\")")]
        [TestCase("clip-path", "path(\"M0 0 C1 2 3\")")]
        [TestCase("width", "var(foo)")]
        [TestCase("width", "var(--foo extra)")]
        [TestCase("width", "inherit auto")]
        [TestCase("does-not-exist", "1px")]
        public void InvalidDeclarationsAreDropped(string property, string input) => Assert.That(Parse(property, input), Is.Null);

        [Test]
        public void CustomPropertiesKeepTokenCaseAndPendingShorthandsKeepSource()
        {
            var custom = Parse("--Mixed", " AbC  1.00px ")!;
            Assert.That(custom.Longhands[0].Longhand, Is.EqualTo("--Mixed"));
            Assert.That(custom.Longhands[0].Value, Is.TypeOf<CssUnparsedValue>());
            Assert.That(custom.Longhands[0].Value.Serialize(), Is.EqualTo("AbC 1.00px"));
            var pending = Parse("margin", "var(--spacing, 2px)")!;
            Assert.That(pending.Longhands.Count, Is.EqualTo(4));
            foreach (var item in pending.Longhands)
            {
                Assert.That(item.Value, Is.TypeOf<CssPendingSubstitutionValue>());
                Assert.That(item.Value.Serialize(), Is.Empty);
            }
            Assert.That(ShorthandSerializer.TrySerialize("margin", name => pending.Longhands.First(x => x.Longhand == name).Value, out string source), Is.True);
            Assert.That(source, Is.EqualTo("var(--spacing, 2px)"));
        }

        [Test]
        public void RawAndPendingStringsPreserveSourceQuoteAndEscapeRepresentation()
        {
            Assert.That(Parse("--raw", "'a\\62 c'")!.Longhands[0].Value.Serialize(), Is.EqualTo("'a\\62 c'"));
            Assert.That(Parse("content", "var(--label, 'fallback')")!.Longhands[0].Value.Serialize(), Is.EqualTo("var(--label, 'fallback')"));
            var shorthand = Parse("background", "var(--image, url('image.png'))")!;
            Assert.That(ShorthandSerializer.TrySerialize("background", name => shorthand.Longhands.First(p => p.Longhand == name).Value,
                out string source), Is.True);
            Assert.That(source, Is.EqualTo("var(--image, url('image.png'))"));
            Assert.That(CssTypeParser.TryParse("string", CssParser.ParseComponentValueList("'typed'"), out CssValue? typed), Is.True);
            Assert.That(typed!.Serialize(), Is.EqualTo("\"typed\""));
        }

        [Test]
        public void AllExpandsWideKeywordAndExcludesDirectionAndUnicodeBidi()
        {
            ParsedProperty parsed = Parse("all", "revert-layer")!;
            Assert.That(parsed.Longhands.Count, Is.GreaterThan(200));
            Assert.That(parsed.Longhands.Any(p => p.Longhand is "direction" or "unicode-bidi" or "all"), Is.False);
            Assert.That(parsed.Longhands.All(p => p.Value is CssWideKeyword), Is.True);
        }

        [Test]
        public void RegistryKeepsFullGeneratedProfileAndBrowserLonghandMetadata()
        {
            foreach (string name in CssPropertyRegistry.SupportedNames)
            {
                PropertyDefinition? definition = CssPropertyRegistry.Get(name);
                Assert.That(definition, Is.Not.Null, name);
                Assert.That(definition!.Syntax, Is.Not.Empty, name);
            }
            foreach (string name in new[] { "box-shadow", "animation-delay", "text-align", "overflow-clip-margin", "-webkit-line-clamp" })
                Assert.That(CssPropertyRegistry.Get(name)!.IsShorthand, Is.False, name);
            Assert.That(CssPropertyRegistry.Get("-webkit-flex")!.Name, Is.EqualTo("flex"));
            Assert.That(CssPropertyRegistry.Get("margin-inline-start")!.LogicalGroup, Is.EqualTo("margin"));
            foreach (string name in new[] { "border-top-left-radius", "border-top-right-radius", "border-bottom-left-radius", "border-bottom-right-radius",
                "animation-trigger", "border-shape", "text-decoration-inset", "clip" })
                Assert.That(CssPropertyRegistry.Get(name), Is.Not.Null, "Shipped property cannot be discarded as a draft: " + name);
        }

        public static IEnumerable<string> LonghandNames() => CssPropertyRegistry.SupportedNames.Where(name => !CssPropertyRegistry.Get(name)!.IsShorthand);

        [TestCaseSource(nameof(LonghandNames))]
        public void EverySupportedLonghandHasAParseableInitialValue(string name)
        {
            PropertyDefinition definition = CssPropertyRegistry.Get(name)!;
            Assert.That(definition.Initial, Is.Not.Empty, name);
            ParsedProperty? initial = Parse(name, definition.Initial);
            Assert.That(initial, Is.Not.Null, name + " initial: " + definition.Initial);
            Assert.That(initial!.Longhands.Count, Is.EqualTo(1));
            Assert.That(initial.Longhands[0].Longhand, Is.EqualTo(name));
        }
        private static ParsedProperty? Parse(string property, string input) => CssPropertyParser.Parse(property, CssParser.ParseComponentValueList(input));
    }
}
