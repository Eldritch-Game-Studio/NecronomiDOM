using NecronomiDom.Css.Media;
using NUnit.Framework;

namespace NecronomiDom.Tests.Media
{
    [TestFixture]
    public sealed class QueryParserTests
    {
        [TestCase("ALL and (min-WIDTH:05PX)", "(min-width: 5px)")]
        [TestCase("screen and (width>=600px)", "screen and (width >= 600px)")]
        [TestCase("(400px<width<=800px)", "(400px < width <= 800px)")]
        [TestCase("(width)", "(width)")]
        [TestCase("(aspect-ratio:16/9)", "(aspect-ratio: 16 / 9)")]
        [TestCase("not (color)", "not (color)")]
        [TestCase("(color) or (monochrome)", "(color) or (monochrome)")]
        [TestCase("(prefers-color-scheme:DARK)", "(prefers-color-scheme: dark)")]
        [TestCase("(-necronomidom-input:gamepad), (-necronomidom-platform:deck)", "(-necronomidom-input: gamepad), (-necronomidom-platform: deck)")]
        [TestCase("(--MixedCase)", "(--MixedCase)")]
        [TestCase("", "")]
        [TestCase("screen,", "screen, not all")]
        [TestCase("screen or (color)", "not all")]
        [TestCase("(color) and (width) or (height)", "not all")]
        [TestCase("not not (color)", "not all")]
        public void MediaGrammarSerializesAndRecoversEachCommaEntry(string source, string expected) => Assert.That(MediaQueryParser.Parse(source).Serialize(), Is.EqualTo(expected));
        [Test]
        public void RangeAstRetainsBothComparisons()
        {
            var condition = (FeatureCondition)MediaQueryParser.Parse("(1px <= width < 9px)").Queries[0].Condition!;
            Assert.That(condition.Name, Is.EqualTo("width"));
            Assert.That(condition.LeftValue, Is.EqualTo("1px"));
            Assert.That(condition.LeftComparison, Is.EqualTo("<="));
            Assert.That(condition.Comparison, Is.EqualTo("<"));
            Assert.That(condition.Value, Is.EqualTo("9px"));
        }
        [TestCase("(display:grid)", "(display: grid)")]
        [TestCase("not (display: grid)", "not (display: grid)")]
        [TestCase("(display:grid) and selector(.a > .b)", "(display: grid) and selector(.a > .b)")]
        [TestCase("font-format(woff2)", "font-format(woff2)")]
        [TestCase("future(foo)", "future(foo)")]
        public void SupportsGrammarPreservesStructuredConditions(string source, string expected) => Assert.That(SupportsParser.Parse(source)?.Serialize(), Is.EqualTo(expected));
        [TestCase("display:grid")]
        [TestCase("(display:grid) or (color:red) and (width:1px)")]
        [TestCase("not (display:grid) and (width:1px)")]
        [TestCase("font-tech()")]
        [TestCase("(display:grid) junk")]
        public void InvalidSupportsConditionIsRejected(string source) => Assert.That(SupportsParser.Parse(source), Is.Null);
        [TestCase("sidebar (width > 10px)", "sidebar (width > 10px)")]
        [TestCase("(width>10px) and (height>=20px)", "(width > 10px) and (height >= 20px)")]
        [TestCase("theme style(--color: blue)", "theme style(--color: blue)")]
        [TestCase("style(--theme)", "style(--theme)")]
        [TestCase("style((--x: red) or (--x: blue))", "style((--x: red) or (--x: blue))")]
        public void ContainerSizeAndStyleQueryGrammars(string source, string expected) => Assert.That(ContainerQueryParser.Parse(source)?.Serialize(), Is.EqualTo(expected));
        [TestCase("none (width > 1px)")]
        [TestCase("sidebar")]
        [TestCase("sidebar width > 1px")]
        [TestCase("style()")] 
        public void MalformedContainersAreRejected(string source) => Assert.That(ContainerQueryParser.Parse(source), Is.Null);
        [Test]
        public void ForwardCompatibleGeneralEnclosedIsNotMistakenForKnownFeature()
        { Assert.That(SupportsParser.Parse("future(foo)"), Is.TypeOf<GeneralEnclosedCondition>()); Assert.That(MediaQueryParser.Parse("(unknown tokens)").Queries[0].Condition, Is.TypeOf<GeneralEnclosedCondition>()); }
    }
}
