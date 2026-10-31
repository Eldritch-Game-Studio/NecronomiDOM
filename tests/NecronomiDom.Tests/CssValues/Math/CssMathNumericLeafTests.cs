using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathNumericLeafTests
    {
        [TestCase("calc(attr(data-size px) + 2px)", "length")]
        [TestCase("calc(attr(data-size type(<length>)) + 2px)", "length")]
        [TestCase("calc(attr(data-value number) * 2)", "number")]
        [TestCase("calc(attr(data-opacity %) / 2)", "percentage")]
        [TestCase("calc(attr(data-size type(<length-percentage>)) + 2px)", "length-percentage")]
        [TestCase("calc(anchor(--target left, 10px) + 2px)", "length")]
        [TestCase("calc(anchor-size(--target width, 10px) * 2)", "length")]
        public void AcceptsOnlyValidatedNumericLeaves(string input, string type)
        {
            CssMathValue math = CssMathParserTests.Parse(input);
            Assert.That(math.Fits(type), Is.True);
            Assert.That(math.TryGetConstant(out _), Is.False);
        }

        [TestCase("calc(attr(data-value) + 2)")]
        [TestCase("calc(attr(data-value type(<color>)) + 2)")]
        [TestCase("calc(attr(data-value imaginary-unit) + 2)")]
        [TestCase("calc(attr(data-value type(<time>)) + 2px)")]
        [TestCase("calc(anchor(unknown) + 2px)")]
        [TestCase("calc(anchor-size(--target unknown) + 2px)")]
        public void RejectsNonNumericOrInvalidLeafGrammar(string input)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue(input)!;
            Assert.That(CssMathParser.TryParse(function, out _), Is.False);
        }

        [Test]
        public void NumericLeavesResolveThroughExplicitContext()
        {
            CssMathValue math = CssMathParserTests.Parse("calc(attr(data-size px) + 2px)");
            var context = new CssMathEvaluationContext(resolveNumericValue: value =>
                value is CssAttrValue { Name: "data-size", Unit: "px" } ? 20 : null);
            Assert.That(math.TryEvaluate(context, out double result), Is.True);
            Assert.That(result, Is.EqualTo(22));
            Assert.That(math.Simplify(context).Serialize(), Is.EqualTo("calc(22px)"));
        }

        [Test]
        public void PercentageNormalizationRequiresAndPropagatesItsDimension()
        {
            CssMathValue math = CssMathParserTests.Parse("min(10%, 20%)");
            Assert.That(math.Simplify(new CssMathEvaluationContext(percentageBasis: -200)).Serialize(),
                Is.EqualTo("min(10%, 20%)"));
            Assert.That(math.Simplify(new CssMathEvaluationContext(percentageBasis: -200,
                percentageHint: CssNumericDimension.Length)).Serialize(), Is.EqualTo("calc(-40px)"));
            math = CssMathParserTests.Parse("calc(10% + 1px)");
            Assert.That(math.Simplify(new CssMathEvaluationContext(percentageBasis: 200)).Serialize(),
                Is.EqualTo("calc(21px)"));
        }
    }
}
