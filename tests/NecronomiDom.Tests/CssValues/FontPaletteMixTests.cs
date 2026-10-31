using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class FontPaletteMixTests
    {
        [TestCase("palette-mix(light 30%, dark)", "palette-mix(light 30%, dark 70%)")]
        [TestCase("palette-mix(in oklab, 30% --Foo, normal)", "palette-mix(--Foo 30%, normal 70%)")]
        [TestCase("palette-mix(in oklab, light 30%, light)", "palette-mix(light 30%, light 70%)")]
        [TestCase("palette-mix(in srgb, light 50%, dark 50%)", "palette-mix(in srgb, light, dark)")]
        [TestCase("palette-mix(in srgb, light 10%, dark 90%)", "palette-mix(in srgb, light 10%, dark 90%)")]
        [TestCase("palette-mix(in srgb, light 50%, dark 40%)", "palette-mix(in srgb, light 50%, dark 40%)")]
        [TestCase("palette-mix(in srgb, light 40%, dark 50%)", "palette-mix(in srgb, light 40%, dark 50%)")]
        [TestCase("palette-mix(in srgb, light 30%, dark 40%)", "palette-mix(in srgb, light 30%, dark 40%)")]
        [TestCase("palette-mix(in srgb, light 75%, dark 75%)", "palette-mix(in srgb, light 75%, dark 75%)")]
        [TestCase("palette-mix(in srgb, light 0%, dark 0%)", "palette-mix(in srgb, light 0%, dark 0%)")]
        [TestCase("palette-mix(in srgb, light 50%, dark)", "palette-mix(in srgb, light, dark)")]
        [TestCase("palette-mix(in srgb, light, dark 90%)", "palette-mix(in srgb, light 10%, dark 90%)")]
        [TestCase("palette-mix(in srgb, light, dark)", "palette-mix(in srgb, light, dark)")]
        [TestCase("palette-mix(in srgb, dark)", "palette-mix(in srgb, dark)")]
        [TestCase("palette-mix(in srgb, dark 100%)", "palette-mix(in srgb, dark)")]
        [TestCase("palette-mix(in srgb, dark 50%)", "palette-mix(in srgb, dark 50%)")]
        [TestCase("palette-mix(in srgb, dark 0%)", "palette-mix(in srgb, dark 0%)")]
        [TestCase("palette-mix(in oklab, dark, light, --foo)", "palette-mix(dark, light, --foo)")]
        [TestCase("palette-mix(in srgb, dark 50%, light, --foo)", "palette-mix(in srgb, dark 50%, light 25%, --foo 25%)")]
        [TestCase("palette-mix(in srgb, dark, light 25%, --foo 25%)", "palette-mix(in srgb, dark 50%, light 25%, --foo 25%)")]
        [TestCase("palette-mix(in srgb, dark, light, --foo, --bar)", "palette-mix(in srgb, dark, light, --foo, --bar)")]
        [TestCase("palette-mix(in srgb, dark 0%, light 0%, --foo 0%)", "palette-mix(in srgb, dark 0%, light 0%, --foo 0%)")]
        [TestCase("palette-mix(palette-mix(in srgb, light 30%, normal) 20%, dark)", "palette-mix(palette-mix(in srgb, light 30%, normal 70%) 20%, dark 80%)")]
        [TestCase("palette-mix(palette-mix(in srgb, light 30%, normal) 30%, palette-mix(in srgb, --foo 30%, dark))", "palette-mix(palette-mix(in srgb, light 30%, normal 70%) 30%, palette-mix(in srgb, --foo 30%, dark 70%) 70%)")]
        [TestCase("palette-mix(in oklch shorter hue, light 30%, dark)", "palette-mix(in oklch, light 30%, dark 70%)")]
        [TestCase("palette-mix(in srgb, dark 50%, light 30%, --foo 20%)", "palette-mix(in srgb, dark 50%, light 30%, --foo 20%)")]
        [TestCase("palette-mix(in srgb, dark 30%, light 60%, --foo 90%)", "palette-mix(in srgb, dark 30%, light 60%, --foo 90%)")]
        public void LiteralWeightsSerializeInSpecifiedAndComputedStyle(string input, string expected)
        {
            CssValue value = Parse(input);
            Assert.That(value.Serialize(), Is.EqualTo(expected));
            Assert.That(ComputedValueResolver.Resolve("font-palette", value, default).Serialize(), Is.EqualTo(expected));
        }

        [TestCase("palette-mix(in srgb, light calc(10%), dark 50%)", "palette-mix(in srgb, light calc(10%), dark 50%)", "palette-mix(in srgb, light 10%, dark 50%)")]
        [TestCase("palette-mix(in srgb, light 50%, dark calc(10%))", "palette-mix(in srgb, light 50%, dark calc(10%))", "palette-mix(in srgb, light 50%, dark 10%)")]
        [TestCase("palette-mix(in srgb, light calc(10%), dark calc(90%))", "palette-mix(in srgb, light calc(10%), dark calc(90%))", "palette-mix(in srgb, light 10%, dark 90%)")]
        [TestCase("palette-mix(in srgb, light calc(50%), dark)", "palette-mix(in srgb, light calc(50%), dark)", "palette-mix(in srgb, light, dark)")]
        [TestCase("palette-mix(in srgb, light, dark calc(50%))", "palette-mix(in srgb, light, dark calc(50%))", "palette-mix(in srgb, light, dark)")]
        [TestCase("palette-mix(in srgb, light calc(10%), dark)", "palette-mix(in srgb, light calc(10%), dark)", "palette-mix(in srgb, light 10%, dark 90%)")]
        [TestCase("palette-mix(in srgb, light, dark calc(90%))", "palette-mix(in srgb, light, dark calc(90%))", "palette-mix(in srgb, light 10%, dark 90%)")]
        [TestCase("palette-mix(in srgb, light calc(-10%), dark)", "palette-mix(in srgb, light calc(-10%), dark)", "palette-mix(in srgb, light 0%, dark 100%)")]
        [TestCase("palette-mix(in srgb, light calc(150%), dark)", "palette-mix(in srgb, light calc(150%), dark)", "palette-mix(in srgb, light 100%, dark 0%)")]
        public void PercentageMathRemainsSpecifiedUntilComputed(string input, string specified, string computed)
        {
            CssValue value = Parse(input);
            Assert.That(value.Serialize(), Is.EqualTo(specified));
            Assert.That(ComputedValueResolver.Resolve("font-palette", value, default).Serialize(), Is.EqualTo(computed));
        }

        private static IEnumerable<TestCaseData> InterpolationCases()
        {
            foreach (string space in new[] { "srgb", "srgb-linear", "display-p3", "display-p3-linear", "a98-rgb", "prophoto-rgb", "rec2020", "lab", "xyz", "xyz-d50", "xyz-d65", "oklab" })
                yield return new TestCaseData(space, "shorter", space == "xyz" ? "xyz-d65" : space);
            foreach (string space in new[] { "hsl", "hwb", "lch", "oklch" })
                foreach (string hue in new[] { "shorter", "longer", "increasing", "decreasing" })
                    yield return new TestCaseData(space, hue, space);
        }

        [TestCaseSource(nameof(InterpolationCases))]
        public void CanonicalInterpolationMetadataIsRetained(string space, string hue, string canonical)
        {
            string method = hue == "shorter" ? "" : " " + hue + " hue";
            var value = (CssPaletteMixValue)Parse($"palette-mix(in {space}{method}, light 10%, dark)");
            string expected = canonical == "oklab" ? "palette-mix(light 10%, dark 90%)" : $"palette-mix(in {canonical}{method}, light 10%, dark 90%)";
            Assert.That(value.Space, Is.EqualTo(canonical));
            Assert.That(value.HueMethod, Is.EqualTo(hue));
            Assert.That(value.Serialize(), Is.EqualTo(expected));
            Assert.That(ComputedValueResolver.Resolve("font-palette", value, default).Serialize(), Is.EqualTo(expected));
        }

        [TestCase("palette-mix()")]
        [TestCase("palette-mix(in oklab)")]
        [TestCase("palette-mix(in oklab,)")]
        [TestCase("palette-mix(in oklab, light,)")]
        [TestCase("palette-mix(in oklab, light -10%, dark)")]
        [TestCase("palette-mix(in oklab, light 150%, dark)")]
        [TestCase("palette-mix(in oklab light 60%, dark 40%)")]
        [TestCase("palette-mix(in oklab, light 60% dark 40%)")]
        [TestCase("palette-mix(oklab, light 60%, dark 40%)")]
        [TestCase("palette-mix(light, dark, in oklab)")]
        [TestCase("palette-mix(in oklch hue, light, dark)")]
        [TestCase("palette-mix(in oklch shorter, light, dark)")]
        [TestCase("palette-mix(in oklch foo hue, light, dark)")]
        [TestCase("palette-mix(in oklab longer hue, light, dark)")]
        [TestCase("palette-mix(in oklab, light, , dark)")]
        [TestCase("palette-mix(in oklab, light, dark foo)")]
        [TestCase("palette-mix(in oklab, 30% 40%, dark)")]
        [TestCase("palette-mix(in oklab, light calc(10px), dark)")]
        [TestCase("palette-mix(in oklab, none, dark)")]
        [TestCase("palette-mix(in oklab, inherit, dark)")]
        public void MalformedMixIsRejected(string input)
        {
            var tokens = CssParser.ParseComponentValueList(input);
            Assert.That(CssFontPaletteParser.TryParse((FunctionValue)tokens.Single(), out _), Is.False);
            Assert.That(CssPropertyParser.Parse("font-palette", tokens), Is.Null);
        }

        [Test]
        public void HostReceivesComputedWeightsWithoutInventingPaletteResources()
        {
            CssValue value = Parse("palette-mix(in oklch longer hue, --Unknown calc(sign(1rem - 1px) * 10%), dark calc(40%))");
            CssValue? received = null;
            var context = new ComputedValueContext
            {
                RootFont = new ComputedFontMetrics(Size: 16),
                FontPaletteValue = palette => { received = palette; return null; }
            };
            var computed = (CssPaletteMixValue)ComputedValueResolver.Resolve("font-palette", value, context);
            Assert.That(received, Is.SameAs(computed));
            Assert.That(computed.Space, Is.EqualTo("oklch"));
            Assert.That(computed.HueMethod, Is.EqualTo("longer"));
            Assert.That(computed.Items[0].Palette.Serialize(), Is.EqualTo("--Unknown"));
            Assert.That(computed.Items[0].Percentage, Is.TypeOf<CssPercentageValue>());
            Assert.That(computed.Serialize(), Is.EqualTo("palette-mix(in oklch longer hue, --Unknown 10%, dark 40%)"));
        }

        [Test]
        public void MissingNumericBasisAndPaletteResourcesStaySymbolic()
        {
            CssValue value = Parse("palette-mix(in srgb, --Unknown calc(sign(1rem - 1px) * 10%), dark)");
            var computed = (CssPaletteMixValue)ComputedValueResolver.Resolve("font-palette", value, default);
            Assert.That(computed.Items[0].Percentage, Is.TypeOf<CssMathValue>());
            Assert.That(computed.Items[1].Percentage, Is.Null);
            Assert.That(computed.Items[0].Palette.Serialize(), Is.EqualTo("--Unknown"));
        }

        private static CssValue Parse(string input)
        {
            ParsedProperty? parsed = CssPropertyParser.Parse("font-palette", CssParser.ParseComponentValueList(input));
            Assert.That(parsed, Is.Not.Null, input);
            CssValue value = parsed!.Longhands.Single().Value;
            Assert.That(value, Is.TypeOf<CssPaletteMixValue>());
            return value;
        }
    }
}
