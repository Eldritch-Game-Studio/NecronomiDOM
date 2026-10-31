using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssSizingCalculationTests
    {
        [TestCase("calc-size(auto, size)", "calc-size(auto, size)")]
        [TestCase("calc-size(min-content, size * 2)", "calc-size(min-content, 2 * size)")]
        [TestCase("calc-size(max-content, size / 2)", "calc-size(max-content, 0.5 * size)")]
        [TestCase("calc-size(fit-content, 30px + size / 2)", "calc-size(fit-content, 30px + (0.5 * size))")]
        [TestCase("calc-size(any, 50px + 30%)", "calc-size(any, 30% + 50px)")]
        [TestCase("calc-size(calc-size(2in, 30px), 25em)", "calc-size(calc-size(192px, 30px), 25em)")]
        [TestCase("calc-size(calc-size(any, 30px), size)", "calc-size(calc-size(any, 30px), size)")]
        [TestCase("calc-size(10px, sign(size) * size)", "calc-size(10px, sign(size) * size)")]
        [TestCase("calc-size(0px, 0px)", "calc-size(0px, 0px)")]
        public void ParsesAndSerializesSizingCalculations(string input, string expected)
        {
            Assert.That(Parse(input).Serialize(), Is.EqualTo(expected));
        }

        [TestCase("calc-size(any, size)")]
        [TestCase("calc-size(none, size)")]
        [TestCase("calc-size(unknown, size)")]
        [TestCase("calc-size(size, 10px)")]
        [TestCase("calc-size(auto, fit-content)")]
        [TestCase("calc-size(any, calc-size(any, 10px))")]
        [TestCase("calc-size(0, 0px)")]
        [TestCase("calc-size(0px, 0)")]
        [TestCase("calc-size(30px)")]
        [TestCase("calc-size(any)")]
        [TestCase("calc-size(30px, 10px, 20px)")]
        [TestCase("calc-size(1s, size)")]
        [TestCase("calc-size(auto, size + 1s)")]
        [TestCase("calc-size(auto, size+ 10px)")]
        public void RejectsInvalidSizingGrammar(string input)
        {
            Assert.That(CssMathParser.TryParseSizing((FunctionValue)CssParser.ParseComponentValue(input)!, out _), Is.False);
        }

        [Test]
        public void SizingCalculationsAreNotOrdinaryMathLengths()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(calc-size(auto, size))")!;
            Assert.That(CssMathParser.TryParse(function, out _), Is.False);
            Assert.That(Parse("calc-size(auto, size)").FitsProperty("max-width"), Is.False);
            Assert.That(Parse("calc-size(auto, size)").FitsProperty("min-width"), Is.True);
            Assert.That(Parse("calc-size(content, size)").FitsProperty("width"), Is.False);
            Assert.That(Parse("calc-size(content, size)").FitsProperty("flex-basis"), Is.True);
        }

        [Test]
        public void ResolvesIntrinsicSizeOnlyAtUsedValueTime()
        {
            CssSizingCalculationValue value = Parse("calc-size(auto, size / 2 + 10px)");
            var context = new CssMathEvaluationContext(resolveNumericValue: basis => basis is CssKeywordValue { Value: "auto" } ? 100 : null);
            Assert.That(value.TryCompute(context, out _), Is.False);
            Assert.That(value.TryEvaluate(context, out double result), Is.True);
            Assert.That(result, Is.EqualTo(60));
            Assert.That(value.Simplify(context).Serialize(), Is.EqualTo("calc-size(auto, 10px + (0.5 * size))"));
        }

        [Test]
        public void ComputesNumericBasisWithoutLosingPercentageDefiniteness()
        {
            CssSizingCalculationValue value = Parse("calc-size(30px, size / 2 + 10px)");
            Assert.That(value.TryCompute(new CssMathEvaluationContext(), out double result), Is.True);
            Assert.That(result, Is.EqualTo(25));
            value = Parse("calc-size(any, 50%)");
            Assert.That(value.TryCompute(new CssMathEvaluationContext(), out _), Is.False);
            Assert.That(value.TryEvaluate(new CssMathEvaluationContext(), out result), Is.True);
            Assert.That(result, Is.Zero);
            value = Parse("calc-size(50%, size)");
            Assert.That(value.TryEvaluate(new CssMathEvaluationContext(), out _), Is.False);
        }

        [Test]
        public void NormalizesNumericBasisAndCalculationButPreservesSizeSymbol()
        {
            CssSizingCalculationValue value = Parse("calc-size(2em, size + 1rem)");
            var context = new CssMathEvaluationContext(unit => unit == "em" ? 20 : unit == "rem" ? 16 : null);
            Assert.That(value.Simplify(context).Serialize(), Is.EqualTo("calc-size(40px, 16px + size)"));
        }

        [Test]
        public void NormalizesPublicLiteralBasisWithoutResolvingCalculationPercentages()
        {
            var percent = CssNumericType.For(CssNumericDimension.Percentage);
            var calculation = new CssMathValue(new CssMathLiteral(10, "%", percent), percent);
            var value = new CssSizingCalculationValue(new CssDimensionValue(2, "em"), calculation);
            CssSizingCalculationValue normalized = value.Simplify(new CssMathEvaluationContext(unit => unit == "em" ? 20 : null));
            Assert.That(normalized.Basis.Serialize(), Is.EqualTo("40px"));
            Assert.That(normalized.Calculation.Serialize(), Does.Contain("10%"));
        }

        [Test]
        public void EvaluatesPublicPrimitiveBasesWithoutAnExternalNumericResolver()
        {
            CssMathValue calculation = Parse("calc-size(10px, size / 2)").Calculation;
            var value = new CssSizingCalculationValue(new CssDimensionValue(40, "px"), calculation);
            Assert.That(value.TryCompute(new CssMathEvaluationContext(), out double result), Is.True);
            Assert.That(result, Is.EqualTo(20));
            value = new CssSizingCalculationValue(new CssPercentageValue(25), calculation);
            Assert.That(value.TryCompute(new CssMathEvaluationContext(), out _), Is.False);
            Assert.That(value.TryEvaluate(new CssMathEvaluationContext(), out _), Is.False);
            Assert.That(value.TryEvaluate(new CssMathEvaluationContext(percentageBasis: 200), out result), Is.True);
            Assert.That(result, Is.EqualTo(25));
        }

        private static CssSizingCalculationValue Parse(string input)
        {
            Assert.That(CssMathParser.TryParseSizing((FunctionValue)CssParser.ParseComponentValue(input)!, out CssValue? value), Is.True, input);
            Assert.That(value, Is.TypeOf<CssSizingCalculationValue>());
            return (CssSizingCalculationValue)value!;
        }
    }
}
