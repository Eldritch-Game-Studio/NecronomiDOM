using System;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathSemanticsTests
    {
        [TestCase("calc(2 + 3 * 4)", 14)]
        [TestCase("calc((2 + 3) * 4)", 20)]
        [TestCase("min(3, 2, 1)", 1)]
        [TestCase("max(1, 2, 3)", 3)]
        [TestCase("clamp(10, 5, 1)", 10)]
        [TestCase("clamp(none, 2, 1)", 1)]
        [TestCase("clamp(3, 2, none)", 3)]
        [TestCase("clamp(none, 2, none)", 2)]
        [TestCase("round(2.5)", 3)]
        [TestCase("round(-2.5)", -2)]
        [TestCase("round(up, 2.1, 1)", 3)]
        [TestCase("round(down, -2.1, 1)", -3)]
        [TestCase("round(to-zero, -2.9, 1)", -2)]
        [TestCase("round(nearest, 5, -2)", 6)]
        [TestCase("mod(-5, 3)", 1)]
        [TestCase("mod(5, -3)", -1)]
        [TestCase("rem(-5, 3)", -2)]
        [TestCase("rem(5, -3)", 2)]
        [TestCase("abs(-3px)", 3)]
        [TestCase("sign(-3px)", -1)]
        [TestCase("sin(90deg)", 1)]
        [TestCase("cos(180deg)", -1)]
        [TestCase("tan(45deg)", 1)]
        [TestCase("sin(pi / 2)", 1)]
        [TestCase("asin(1)", 90)]
        [TestCase("acos(-1)", 180)]
        [TestCase("atan(1)", 45)]
        [TestCase("atan2(1px, -1px)", 135)]
        [TestCase("pow(2, 3)", 8)]
        [TestCase("sqrt(9)", 3)]
        [TestCase("hypot(3px, 4px)", 5)]
        [TestCase("log(8, 2)", 3)]
        [TestCase("log(e)", 1)]
        [TestCase("exp(0)", 1)]
        [TestCase("calc(1in + 72pt)", 192)]
        [TestCase("calc(1s + 500ms)", 1.5)]
        [TestCase("calc(1turn + 100grad)", 450)]
        [TestCase("calc(96dpi + 1x)", 2)]
        public void EvaluatesAllFunctionFamilies(string input, double expected)
        {
            CssMathValue math = CssMathParserTests.Parse(input);
            Assert.That(math.TryGetConstant(out double value), Is.True);
            Assert.That(value, Is.EqualTo(expected).Within(1e-12));
        }

        [TestCase("calc(0 / 0)")]
        [TestCase("calc(infinity - infinity)")]
        [TestCase("round(1, 0)")]
        [TestCase("round(infinity, infinity)")]
        [TestCase("mod(-1, infinity)")]
        [TestCase("rem(infinity, 1)")]
        [TestCase("sin(infinity)")]
        [TestCase("asin(2)")]
        [TestCase("pow(-1, 0.5)")]
        [TestCase("pow(NaN, 0)")]
        [TestCase("pow(1, infinity)")]
        [TestCase("hypot(infinity, NaN)")]
        [TestCase("sqrt(-1)")]
        [TestCase("log(-1)")]
        [TestCase("log(2, 1)")]
        public void NaNIsInfectiousInsideTheTree(string input)
        {
            Assert.That(CssMathParserTests.Parse(input).TryGetConstant(out double value), Is.True);
            Assert.That(double.IsNaN(value), Is.True);
        }

        [TestCase("calc(1 / sign(-0))", double.NegativeInfinity)]
        [TestCase("calc(1 / min(0, -0))", double.NegativeInfinity)]
        [TestCase("calc(1 / max(0, -0))", double.PositiveInfinity)]
        [TestCase("calc(1 / round(-0.1, 1))", double.NegativeInfinity)]
        [TestCase("calc(1 / mod(3, -3))", double.NegativeInfinity)]
        [TestCase("rem(2, infinity)", 2)]
        [TestCase("round(up, 2, infinity)", double.PositiveInfinity)]
        [TestCase("round(down, -2, infinity)", double.NegativeInfinity)]
        [TestCase("log(0)", double.NegativeInfinity)]
        public void PreservesIeeeSpecialValues(string input, double expected)
        {
            Assert.That(CssMathParserTests.Parse(input).TryGetConstant(out double value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        [Test]
        public void ResolvesRelativeUnitsAndPercentagesAtUsedValueTime()
        {
            CssMathValue math = CssMathParserTests.Parse("clamp(1em, 50% + 2px, 10rem)");
            Assert.That(math.TryGetConstant(out _), Is.False);
            var context = new CssMathEvaluationContext(unit => unit switch { "em" => 20, "rem" => 16, _ => null },
                200, CssNumericDimension.Length);
            Assert.That(math.TryEvaluate(context, out double value), Is.True);
            Assert.That(value, Is.EqualTo(102));
            Assert.That(math.Simplify(context).Serialize(), Is.EqualTo("calc(102px)"));
        }

        [Test]
        public void NegativePercentageBasisReversesComparisons()
        {
            CssMathValue math = CssMathParserTests.Parse("min(10%, 20%)");
            Assert.That(math.TryEvaluate(new CssMathEvaluationContext(percentageBasis: -200), out double value), Is.True);
            Assert.That(value, Is.EqualTo(-40));
        }

        [TestCase("round(line-width, 0.2px)", 0.5)]
        [TestCase("round(line-width, -0.2px)", -0.5)]
        [TestCase("round(line-width, 1.4px)", 1)]
        [TestCase("round(line-width, 0.2px, 1px)", 1)]
        public void LineWidthStrategyUsesDevicePixels(string input, double expected)
        {
            CssMathValue math = CssMathParserTests.Parse(input);
            Assert.That(math.TryGetConstant(out _), Is.False);
            Assert.That(math.TryEvaluate(new CssMathEvaluationContext(devicePixelRatio: 2), out double value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }
    }
}
