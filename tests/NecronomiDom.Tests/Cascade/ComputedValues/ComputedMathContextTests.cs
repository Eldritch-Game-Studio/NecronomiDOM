using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    [TestFixture]
    public sealed class ComputedMathContextTests
    {
        [TestCase("animation-delay", "calc(10s + sign(2cqw - 10px) * 5s)", "5s", "10s", "15s")]
        [TestCase("animation-duration", "calc(10s + sign(2cqw - 10px) * 5s)", "5s", "10s", "15s")]
        [TestCase("animation-iteration-count", "calc(10 + sign(2cqw - 10px) * 5)", "5", "10", "15")]
        [TestCase("flex-grow", "calc(10 + sign(2cqw - 10px) * 5)", "5", "10", "15")]
        [TestCase("flex-shrink", "calc(10 + sign(2cqw - 10px) * 5)", "5", "10", "15")]
        [TestCase("filter", "brightness(calc(100% + sign(2cqw - 10px) * 50%))", "brightness(0.5)", "brightness(1)", "brightness(1.5)")]
        [TestCase("filter", "blur(calc(100px + sign(2cqw - 10px) * 50px))", "blur(50px)", "blur(100px)", "blur(150px)")]
        [TestCase("filter", "hue-rotate(calc(90deg + sign(2cqw - 10px) * 45deg))", "hue-rotate(45deg)", "hue-rotate(90deg)", "hue-rotate(135deg)")]
        [TestCase("font-feature-settings", "\"liga\" calc(10 + sign(2cqw - 10px) * 5)", "\"liga\" 5", "\"liga\" 10", "\"liga\" 15")]
        [TestCase("counter-increment", "chapter calc(10 + sign(2cqw - 10px) * 5)", "chapter 5", "chapter 10", "chapter 15")]
        [TestCase("scale", "calc(10 + sign(2cqw - 10px) * 5)", "5", "10", "15")]
        [TestCase("transform", "rotate(calc(90deg * sign(2cqw - 10px)))", "matrix(0, -1, 1, 0, 0, 0)", "matrix(1, 0, 0, 1, 0, 0)", "matrix(0, 1, -1, 0, 0, 0)")]
        public void ContainerDependentNumbersUseEachProvidedAxis(string property, string specified,
            string negative, string zero, string positive)
        {
            CssValue value = ComputedPropertyTests.Parse(property, specified);
            foreach (var scenario in new[] { (250.0, negative), (500.0, zero), (1000.0, positive) })
            {
                var context = ComputedUnitTests.Context with
                {
                    ContainerUnitBasis = unit => unit is "cqw" or "cqi" ? scenario.Item1 : null
                };
                Assert.That(ComputedValueResolver.Resolve(property, value, context).Serialize(), Is.EqualTo(scenario.Item2));
            }
            var unavailable = ComputedUnitTests.Context with { ContainerUnitBasis = null };
            Assert.That(ComputedValueResolver.Resolve(property, value, unavailable).Serialize(), Does.Contain("cqw"));
        }

        [TestCase("font-feature-settings", "\"swsh\" calc(1em / 1px)", "\"swsh\" 16")]
        [TestCase("font-variation-settings", "\"xhgt\" calc(1em / 1px)", "\"xhgt\" 16")]
        [TestCase("font-size-adjust", "calc(1em / 1px)", "16")]
        [TestCase("font-style", "oblique calc(1em / 1px * 1deg)", "oblique 16deg")]
        [TestCase("font-weight", "calc(1em / 1px)", "16")]
        [TestCase("font-width", "calc(1em / 1px * 1%)", "16%")]
        public void FontAffectingNumbersUseParentRatherThanCurrentFont(string property, string specified, string expected)
        {
            var context = ComputedUnitTests.Context with { Font = new(100), ParentFont = new(16) };
            Assert.That(ComputedValueResolver.Resolve(property, ComputedPropertyTests.Parse(property, specified), context).Serialize(),
                Is.EqualTo(expected));
        }

        [TestCase("em", 16, 16)]
        [TestCase("ex", 7, 8)]
        [TestCase("ch", 8, 8)]
        [TestCase("cap", 11, 11)]
        [TestCase("ic", 16, 16)]
        [TestCase("lh", 24, 24)]
        [TestCase("rem", 18, 16)]
        [TestCase("rlh", 26, 24)]
        public void FontMathAvoidsLocalAndRootMetricCycles(string unit, double childExpected, double rootExpected)
        {
            string specified = "calc(1" + unit + " / 1px)";
            CssValue value = ComputedPropertyTests.Parse("font-size-adjust", specified);
            foreach (bool root in new[] { false, true })
            {
                var context = ComputedUnitTests.Context with { IsRoot = root };
                var result = (CssNumberValue)ComputedValueResolver.Resolve("font-size-adjust", value, context);
                Assert.That(result.Value, Is.EqualTo(root ? rootExpected : childExpected));
            }
        }

        [TestCase(-1.5, -1)]
        [TestCase(1.5, 2)]
        [TestCase(4503599627370497.0, 4503599627370497.0)]
        [TestCase(-4503599627370497.0, -4503599627370497.0)]
        public void IntegerRoundingDoesNotIncrementAlreadyIntegralLargeNumbers(double input, double expected)
        {
            var math = new CssMathValue(new CssMathLiteral(input, "", default), default)
                .WithGrammarConstraints(true, double.NegativeInfinity, double.PositiveInfinity);
            var result = (CssIntegerValue)ComputedValueResolver.Resolve("--integer", math, default);
            Assert.That(result.Value, Is.EqualTo(expected));
        }

        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.MaxValue)]
        [TestCase(-double.MaxValue)]
        public void OutOfRangeAnglesClampToFiniteWholeTurns(double input)
        {
            var type = CssNumericType.For(CssNumericDimension.Angle);
            var math = new CssMathValue(new CssMathLiteral(input, "deg", type), type);
            var result = (CssDimensionValue)ComputedValueResolver.Resolve("--angle", math, default);
            Assert.That(double.IsFinite(result.Value), Is.True);
            Assert.That(result.Value % 360, Is.Zero);
            Assert.That(double.IsNegative(result.Value), Is.EqualTo(double.IsNegative(input)));
            Assert.That(double.IsFinite(result.Value * System.Math.PI / 180), Is.True);
            var direct = (CssDimensionValue)ComputedValueResolver.Resolve("--angle", new CssDimensionValue(input, "deg"), default);
            Assert.That(direct.Value, Is.EqualTo(result.Value));
        }

        [TestCase(double.NaN, 0)]
        [TestCase(double.PositiveInfinity, 8)]
        [TestCase(double.NegativeInfinity, -3)]
        public void TopLevelNonfiniteCensoringPrecedesGrammarBounds(double input, double expected)
        {
            var math = new CssMathValue(new CssMathLiteral(input, "", default), default)
                .WithGrammarConstraints(false, -3, 8);
            var result = (CssNumberValue)ComputedValueResolver.Resolve("--number", math, default);
            Assert.That(result.Value, Is.EqualTo(expected));
        }

        [TestCase(1, "steps(2, jump-none)")]
        [TestCase(3, "steps(6, jump-none)")]
        public void EasingStepCountUsesTheSuppliedTreeContext(int siblingIndex, string expected)
        {
            var context = ComputedUnitTests.Context with
            {
                NumericValue = value => value is CssFunctionValue { Name: "sibling-index" } ? siblingIndex : null
            };
            CssValue specified = ComputedPropertyTests.Parse("transition-timing-function", "steps(calc(2 * sibling-index()), jump-none)");
            Assert.That(ComputedValueResolver.Resolve("transition-timing-function", specified, context).Serialize(), Is.EqualTo(expected));
            Assert.That(ComputedValueResolver.Resolve("transition-timing-function", specified, ComputedUnitTests.Context).Serialize(),
                Does.Contain("sibling-index()"));
        }
    }
}
