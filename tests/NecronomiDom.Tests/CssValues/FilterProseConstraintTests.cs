using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class FilterProseConstraintTests
    {
        // Filter Effects 1 function prose constrains ranges beyond Webref's formal grammar.
        // WPT: css/filter-effects/parsing/{backdrop-,}filter-parsing-invalid.html.
        [TestCase("blur(-100px)")]
        [TestCase("brightness(-20)")]
        [TestCase("contrast(-20)")]
        [TestCase("grayscale(-20)")]
        [TestCase("invert(-20)")]
        [TestCase("opacity(-20)")]
        [TestCase("saturate(-20)")]
        [TestCase("sepia(-20)")]
        [TestCase("GrAyScAlE(-1%)")]
        public void LiteralBelowZeroIsInvalidForEitherFilterProperty(string text)
        {
            foreach (string property in new[] { "filter", "backdrop-filter" })
                Assert.That(CssPropertyParser.Parse(property, CssParser.ParseComponentValueList(text)), Is.Null, property);
        }

        // WPT: css/filter-effects/parsing/{backdrop-,}filter-parsing-valid.html.
        [TestCase("grayscale(300%)", "grayscale(100%)")]
        [TestCase("invert(300%)", "invert(100%)")]
        [TestCase("opacity(300%)", "opacity(100%)")]
        [TestCase("opacity(2)", "opacity(1)")]
        [TestCase("sepia(300%)", "sepia(100%)")]
        [TestCase("hue-rotate(0)", "hue-rotate(0deg)")]
        [TestCase("brightness(300%)", "brightness(300%)")]
        [TestCase("contrast(2)", "contrast(2)")]
        [TestCase("saturate(300%)", "saturate(300%)")]
        [TestCase("hue-rotate(-90deg)", "hue-rotate(-90deg)")]
        public void SpecifiedRangeClampPreservesTheFunctionSpecificBounds(string text, string expected)
        {
            foreach (string property in new[] { "filter", "backdrop-filter" })
            {
                ParsedProperty? parsed = CssPropertyParser.Parse(property, CssParser.ParseComponentValueList(text));
                Assert.That(parsed, Is.Not.Null, property);
                Assert.That(parsed!.Longhands[0].Value.Serialize(), Is.EqualTo(expected), property);
            }
        }

        [TestCase("blur(calc(-1px))")]
        [TestCase("brightness(calc(-1))")]
        [TestCase("grayscale(calc(-1%))")]
        public void CalculationRangesAreDeferredUntilComputedValueEvaluation(string text)
        {
            foreach (string property in new[] { "filter", "backdrop-filter" })
                Assert.That(CssPropertyParser.Parse(property, CssParser.ParseComponentValueList(text)), Is.Not.Null, property);
        }
    }
}
