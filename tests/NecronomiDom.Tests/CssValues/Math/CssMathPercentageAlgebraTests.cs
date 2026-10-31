using System;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathPercentageAlgebraTests
    {
        // css/css-values/typed_arithmetic.html: the basis is an explicit evaluation input, not a guessed layout metric.
        [TestCase("calc(10% / 1px)", 10, "number", 1)]
        [TestCase("calc((10% * 1%) / 1px)", 100, "length", 10)]
        [TestCase("calc(10% * 10% / 1px * 10deg / 1deg / 10px)", 10, "number", 1)]
        [TestCase("calc(10% * 10% / 1px * 1deg / 1deg)", 10, "length", 1)]
        [TestCase("calc(1px * pow(50% / 1px, 1))", 100, "length", 50)]
        [TestCase("calc(.25px * round(100% / 25px))", 100, "length", 1)]
        [TestCase("calc(4px / round(100% / 25px))", 100, "length", 1)]
        [TestCase("clamp(110px / 10px * 1px, 1em + 200%, 200% * 1% / 1em)", 100, "length", 20)]
        public void DimensionalPercentagesEvaluateInTheConsumingContext(string input, double basis, string grammar, double expected)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue(input)!;
            Assert.That(CssMathParser.TryParse(function, CssNumericDimension.Length, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            Assert.That(math.Fits(grammar, CssNumericDimension.Length), Is.True);
            var context = new CssMathEvaluationContext(unit => unit == "em" ? 10 : null,
                basis, CssNumericDimension.Length);
            Assert.That(math.TryEvaluate(context, out double result), Is.True);
            Assert.That(result, Is.EqualTo(expected).Within(1e-10));
            Assert.That(math.Simplify(context).TryEvaluate(context, out result), Is.True);
            Assert.That(result, Is.EqualTo(expected).Within(1e-10));
        }

        [TestCase("calc(10% / 20%)")]
        [TestCase("pow(10% / 20%, 2)")]
        [TestCase("sqrt(25% / 1%)")]
        [TestCase("sign(10%)")]
        public void CancelledPercentagesCannotEnterPercentageFreeNumberGrammar(string input)
        {
            CssMathValue math = CssMathParserTests.Parse(input);
            Assert.That(math.Type.PercentageHint, Is.EqualTo(CssNumericDimension.Percentage));
            Assert.That(math.Fits("number"), Is.False);
            Assert.That(math.Fits("integer"), Is.False);
            Assert.That(math.Fits("number", CssNumericDimension.Percentage), Is.True);
            Assert.That(math.Fits("number-percentage"), Is.True);
        }

        [TestCase("calc(-50% - 50%)", "calc(-100%)")]
        [TestCase("min(50%, 0%)", "calc(0%)")]
        [TestCase("max(50%, 0%)", "calc(50%)")]
        [TestCase("clamp(50%, 80%, 70%)", "calc(70%)")]
        [TestCase("calc(25% * 2)", "calc(50%)")]
        [TestCase("calc(100% + 100%)", "calc(200%)")]
        public void NumberPercentageContextPreservesPurePercentageSpecifiedValues(string input, string expected)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue(input)!;
            Assert.That(CssMathParser.TryParse(function, CssNumericDimension.Number, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            Assert.That(math.Serialize(), Is.EqualTo(expected));
            Assert.That(math.Fits("number-percentage"), Is.True);
            Assert.That(math.TryGetConstant(out _), Is.False);
            var context = new CssMathEvaluationContext(percentageHint: CssNumericDimension.Number);
            Assert.That(math.TryEvaluate(context, out _), Is.True);
        }

        [Test]
        public void ComputedZeroAbsoluteTermDoesNotFabricateAPercentageBasis()
        {
            CssMathValue specified = CssMathParserTests.Parse("calc(10% + 0px)");
            Assert.That(specified.Serialize(), Is.EqualTo("calc(10% + 0px)"));
            CssMathValue computed = specified.Simplify(new CssMathEvaluationContext());
            Assert.That(computed.Serialize(), Is.EqualTo("calc(10%)"));
            Assert.That(computed.Root, Is.TypeOf<CssMathLiteral>());
            Assert.That(computed.Root.Type.IsPercentage, Is.True);
            Assert.That(computed.Type.PercentageHint, Is.EqualTo(CssNumericDimension.Length));
            Assert.That(computed.TryGetConstant(out _), Is.False);
        }

        [TestCase("cap")][TestCase("cqi")][TestCase("cqw")][TestCase("em")]
        public void ZeroRelativeUnitsNeedNoInventedMetrics(string unit)
        {
            CssMathValue math = CssMathParserTests.Parse("calc(-0" + unit + ")");
            Assert.That(math.Serialize(), Is.EqualTo("calc(0" + unit + ")"));
            Assert.That(math.TryEvaluate(new CssMathEvaluationContext(), out double result), Is.True);
            Assert.That(BitConverter.DoubleToInt64Bits(result), Is.EqualTo(long.MinValue));
            Assert.That(math.Simplify(new CssMathEvaluationContext()).Serialize(), Is.EqualTo("calc(0px)"));
            CssMathValue nonzero = CssMathParserTests.Parse("calc(1" + unit + ")");
            Assert.That(nonzero.TryEvaluate(new CssMathEvaluationContext(), out _), Is.False);
        }

        [Test]
        public void ProductSerializationPutsNumericPercentBeforeSymbolicFactor()
        {
            CssMathValue math = CssMathParserTests.Parse("calc(sign(1em - 1px) * 10%)");
            Assert.That(math.Serialize(), Is.EqualTo("calc(10% * sign(1em - 1px))"));
        }

        [Test]
        public void AngleArgumentWithContextualPercentageCancellationStillUsesDegrees()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("sin(90deg * (10% / 10%))")!;
            Assert.That(CssMathParser.TryParse(function, CssNumericDimension.Length, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            Assert.That(math.Fits("number", CssNumericDimension.Length), Is.True);
            Assert.That(math.TryGetConstant(out double result), Is.True);
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void NonfiniteMixedPercentageArithmeticNeedsTheGenuineBasis()
        {
            CssMathValue math = CssMathParserTests.Parse("calc(infinity * 1px - infinity * 1%)");
            Assert.That(math.TryGetConstant(out _), Is.False);
            var positive = new CssMathEvaluationContext(percentageBasis: 100, percentageHint: CssNumericDimension.Length);
            Assert.That(math.TryEvaluate(positive, out double result), Is.True);
            Assert.That(double.IsNaN(result), Is.True);
            var negative = new CssMathEvaluationContext(percentageBasis: -100, percentageHint: CssNumericDimension.Length);
            Assert.That(math.TryEvaluate(negative, out result), Is.True);
            Assert.That(double.IsPositiveInfinity(result), Is.True);
        }

        [TestCase("calc((10px / 2s) * 1s)", "calc(5px)")]
        [TestCase("calc((1px / 1s) * 2s)", "calc(2px)")]
        [TestCase("calc(((2px * 3s) + (4px * 1s)) / 1s)", "calc(10px)")]
        [TestCase("calc((1px / 1s) / (1px / 2s))", "calc(2)")]
        [TestCase("calc(calc(10px / 2s) * 1s)", "calc(5px)")]
        [TestCase("calc(abs(-10px / 2s) * 1s)", "calc(5px)")]
        [TestCase("calc(min(10px / 2s, 8px / 2s) * 1s)", "calc(4px)")]
        [TestCase("sign(1px / 1s)", "calc(1)")]
        public void CompositeIntermediateTypesDoNotLoseTheirUnitsDuringSimplification(string input, string expected)
        {
            CssMathValue math = CssMathParserTests.Parse(input);
            Assert.That(math.Serialize(), Is.EqualTo(expected));
        }

        [TestCase(255, 127.75)][TestCase(125, 62.75)][TestCase(1, 0.75)]
        public void ExplicitChannelBasisPreventsWrongNumberPercentagePrefolding(double basis, double expected)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(50% + .25)")!;
            Assert.That(CssMathParser.TryParse(function, CssNumericDimension.Number, basis, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            Assert.That(math.TryGetConstant(out double result), Is.True);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void SymbolicChannelMathUsesTheSuppliedNumericPercentageBasis()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(r + 50%)")!;
            Assert.That(CssMathParser.TryParseChannels(function, new[] { "r" }, CssNumericDimension.Number, 255,
                out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            var context = new CssMathEvaluationContext(resolveNumericValue: value =>
                value is CssKeywordValue { Value: "r" } ? 120 : null);
            Assert.That(math.TryEvaluate(context, out double result), Is.True);
            Assert.That(result, Is.EqualTo(247.5));
        }

        [TestCase("calc(NaN * 1cqw)")]
        [TestCase("calc(1em + NaN * 1px)")]
        [TestCase("calc(min(1em, NaN * 1px))")]
        public void InfectiousNaNDoesNotRequireUnrelatedMissingMetrics(string input)
        {
            CssMathValue math = CssMathParserTests.Parse(input);
            Assert.That(math.TryGetConstant(out double value), Is.True);
            Assert.That(double.IsNaN(value), Is.True);
            Assert.That(math.Simplify(new CssMathEvaluationContext()).Serialize(), Is.EqualTo("calc(NaN * 1px)"));
        }

        [Test]
        public void NaNPercentageCanResolveItsDimensionWithoutAnInventedLayoutBasis()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(NaN * 1%)")!;
            Assert.That(CssMathParser.TryParse(function, CssNumericDimension.Length, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            CssMathValue computed = math.Simplify(new CssMathEvaluationContext());
            Assert.That(computed.Serialize(), Is.EqualTo("calc(NaN * 1px)"));
            Assert.That(computed.TryGetConstant(out double value), Is.True);
            Assert.That(double.IsNaN(value), Is.True);
        }

        [Test]
        public void PercentageRatioIsAppliedBeforeNumericChannelBasis()
        {
            CssMathValue math = CssMathParserTests.Parse("calc(50%)");
            var context = new CssMathEvaluationContext(percentageBasis: 255, percentageHint: CssNumericDimension.Number);
            Assert.That(math.TryEvaluate(context, out double value), Is.True);
            Assert.That(value, Is.EqualTo(127.5));
            Assert.That(math.Simplify(context).TryGetConstant(out value), Is.True);
            Assert.That(value, Is.EqualTo(127.5));
        }

        [Test]
        public void SpecifiedPercentageMagnitudeIdentityDoesNotIntroduceRounding()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(7%)")!;
            Assert.That(CssMathParser.TryParse(function, CssNumericDimension.Number, 255, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            Assert.That(math.Root, Is.TypeOf<CssMathLiteral>());
            Assert.That(((CssMathLiteral)math.Root).Value, Is.EqualTo(7));
            Assert.That(math.Serialize(), Is.EqualTo("calc(7%)"));
        }
    }
}
