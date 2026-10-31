using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    [TestFixture]
    public sealed class ComputedMathTransformTests
    {
        [TestCase("width", "calc(2em + 3px)", "43px")]
        [TestCase("width", "min(2em, 30px)", "30px")]
        [TestCase("width", "max(2em, 30px)", "40px")]
        [TestCase("width", "clamp(5px, 2em, 30px)", "30px")]
        [TestCase("width", "round(23px, 5px)", "25px")]
        [TestCase("width", "mod(-3px, 5px)", "2px")]
        [TestCase("width", "rem(-3px, 5px)", "0px")]
        [TestCase("margin-left", "rem(-3px, 5px)", "-3px")]
        [TestCase("width", "abs(-2em)", "40px")]
        [TestCase("opacity", "calc(sin(30deg))", "0.5")]
        [TestCase("opacity", "calc(sign(2px - 3px))", "0")]
        [TestCase("opacity", "calc(25% + 0.25)", "0.5")]
        [TestCase("font-size", "max(100%, 1em + 1px)", "17px")]
        [TestCase("transform", "none", "none")]
        [TestCase("transform", "translate(2em, 3px) scale(2)", "matrix(2, 0, 0, 2, 40, 3)")]
        [TestCase("transform", "scale(2) translate(2em, 3px)", "matrix(2, 0, 0, 2, 80, 6)")]
        [TestCase("transform", "rotate(0.25turn)", "matrix(0, 1, -1, 0, 0, 0)")]
        [TestCase("transform", "translateX(2em)", "matrix(1, 0, 0, 1, 40, 0)")]
        [TestCase("transform", "translate3d(1px, 2px, 3px)", "matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 2, 3, 1)")]
        [TestCase("rotate", "z 0.5turn", "180deg")]
        [TestCase("scale", "50% 50%", "0.5")]
        [TestCase("translate", "1em 0px 0px", "20px")]
        [TestCase("transition-timing-function", "linear(0, 0.25, 1)", "linear(0 0%, 0.25 50%, 1 100%)")]
        public void TypedMathematicalAndMatrixValues(string property, string specified, string expected)
        {
            Assert.That(ComputedValueResolver.Resolve(property, ComputedPropertyTests.Parse(property, specified), ComputedUnitTests.Context).Serialize(), Is.EqualTo(expected));
        }

        [Test]
        public void MixedLengthPercentageSimplifiesWithoutInventingLayoutBasis()
        {
            CssValue specified = ComputedPropertyTests.Parse("width", "calc(50% + 2em + 3px)");
            CssValue computed = ComputedValueResolver.Resolve("width", specified, ComputedUnitTests.Context);
            Assert.That(computed, Is.TypeOf<CssMathValue>());
            Assert.That(((CssMathValue)computed).TryEvaluate(new CssMathEvaluationContext(percentageBasis: 100), out double result), Is.True);
            Assert.That(result, Is.EqualTo(93));
            Assert.That(computed.Serialize(), Does.Contain("50%").And.Contain("43px").And.Not.Contain("em"));
        }

        [Test]
        public void LayoutDependentTransformTranslationRemainsFunctional()
        {
            CssValue value = ComputedPropertyTests.Parse("transform", "translate(10%, 1em) rotate(1turn)");
            Assert.That(ComputedValueResolver.Resolve("transform", value, ComputedUnitTests.Context).Serialize(), Is.EqualTo("translate(10%, 20px) rotate(360deg)"));
        }

        [Test]
        public void IntegerCalculationRoundsTiesTowardPositiveInfinity()
        {
            Assert.That(ComputedValueResolver.Resolve("order", ComputedPropertyTests.Parse("order", "calc(-1.5)"), ComputedUnitTests.Context).Serialize(), Is.EqualTo("-1"));
        }

        [Test]
        public void SizingCalculationsRetainIntrinsicBehaviorAtComputedTime()
        {
            CssNumericType length = CssNumericType.For(CssNumericDimension.Length);
            var calculation = new CssMathValue(new CssMathLiteral(20, "px", length), length);
            var specified = new CssSizingCalculationValue(new CssKeywordValue("auto"), calculation);
            CssValue computed = ComputedValueResolver.Resolve("width", specified, ComputedUnitTests.Context);
            Assert.That(computed, Is.TypeOf<CssSizingCalculationValue>());
            Assert.That(((CssSizingCalculationValue)computed).Basis.Serialize(), Is.EqualTo("auto"));
            Assert.That(((CssSizingCalculationValue)computed).Calculation.TryGetConstant(out double result), Is.True);
            Assert.That(result, Is.EqualTo(20));
        }

        [Test]
        public void SizingCalculationNormalizesNumericBasisWithoutZeroingUnresolvedPercentage()
        {
            CssNumericType percent = CssNumericType.For(CssNumericDimension.Percentage);
            var calculation = new CssMathValue(new CssMathLiteral(10, "%", percent), percent);
            var specified = new CssSizingCalculationValue(new CssDimensionValue(2, "em"), calculation);
            var computed = (CssSizingCalculationValue)ComputedValueResolver.Resolve("width", specified, ComputedUnitTests.Context);
            Assert.That(computed.Basis.Serialize(), Is.EqualTo("40px"));
            Assert.That(computed.Calculation.Serialize(), Does.Contain("10%"));
        }
    }
}
