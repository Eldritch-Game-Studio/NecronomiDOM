using System;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathUnaryTests
    {
        [TestCase("negate", "calc(-1)", -1)]
        [TestCase("invert", "calc(1 / 1)", 1)]
        public void NativeUnaryNodesHaveTypedOmSerializationAndSemantics(string name, string expected, double result)
        {
            CssMathOperation operation = Unary(name, Number(1));
            var value = new CssMathValue(operation, operation.Type);
            Assert.That(value.Serialize(), Is.EqualTo(expected));
            Assert.That(value.TryEvaluate(new CssMathEvaluationContext(), out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(result));
            Assert.That(value.Simplify(new CssMathEvaluationContext()).TryGetConstant(out actual), Is.True);
            Assert.That(actual, Is.EqualTo(result));
            Assert.That(value.Root, Is.SameAs(operation));
            Assert.That(value.Serialize(), Is.EqualTo(expected));
        }

        [TestCase("+", "negate", true, "calc((-1) + 2)")]
        [TestCase("+", "negate", false, "calc(1 - 2)")]
        [TestCase("*", "invert", true, "calc((1 / 1) * 2)")]
        [TestCase("*", "invert", false, "calc(1 / 2)")]
        public void NestedUnarySerializationUsesSubtractionAndDivision(string outer, string unary, bool first, string expected)
        {
            CssMathExpression left = first ? Unary(unary, Number(1)) : Number(1);
            CssMathExpression right = first ? Number(2) : Unary(unary, Number(2));
            var operation = new CssMathOperation(outer, new[] { left, right }, default);
            Assert.That(new CssMathValue(operation, default).Serialize(), Is.EqualTo(expected));
        }

        [Test]
        public void InvertedRelativeDimensionCancelsWithoutAUnitResolver()
        {
            var length = CssNumericType.For(CssNumericDimension.Length);
            var inverse = new CssMathOperation("invert", new[] { new CssMathLiteral(2, "em", length) },
                new CssNumericType(Length: -1));
            var product = new CssMathOperation("*", new CssMathExpression[] { new CssMathLiteral(6, "em", length), inverse }, default);
            var original = new CssMathValue(product, default);
            Assert.That(original.Serialize(), Is.EqualTo("calc(6em / 2em)"));
            CssMathValue simplified = original.Simplify(new CssMathEvaluationContext());
            Assert.That(simplified.Serialize(), Is.EqualTo("calc(3)"));
            Assert.That(simplified.TryGetConstant(out double value), Is.True);
            Assert.That(value, Is.EqualTo(3));
            Assert.That(original.Root, Is.SameAs(product));
        }

        [Test]
        public void InversionRetainsNegativeDimensionPowers()
        {
            CssNumericType length = CssNumericType.For(CssNumericDimension.Length);
            var inverse = new CssMathOperation("invert", new[] { new CssMathLiteral(2, "em", length) },
                new CssNumericType(Length: -1));
            var value = new CssMathValue(inverse, inverse.Type);
            Assert.That(value.Type.Length, Is.EqualTo(-1));
            Assert.That(value.TryEvaluate(new CssMathEvaluationContext(unit => unit == "em" ? 16 : null), out double result), Is.True);
            Assert.That(result, Is.EqualTo(1.0 / 32));
            Assert.That(value.Simplify(new CssMathEvaluationContext()).Serialize(), Is.EqualTo("calc(1 / 2em)"));
        }

        [Test]
        public void UnarySignedZeroSemanticsArePreserved()
        {
            CssMathOperation negate = Unary("negate", Number(0));
            Assert.That(negate.TryEvaluate(new CssMathEvaluationContext(), out double value), Is.True);
            Assert.That(BitConverter.DoubleToInt64Bits(value), Is.EqualTo(long.MinValue));
            CssMathOperation invert = Unary("invert", Number(-0.0));
            Assert.That(invert.TryEvaluate(new CssMathEvaluationContext(), out value), Is.True);
            Assert.That(double.IsNegativeInfinity(value), Is.True);
        }

        [TestCase("negate")][TestCase("invert")]
        public void TypedOmOperatorsAreNotInventedCssFunctions(string name)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue(name + "(1)")!;
            Assert.That(CssMathParser.TryParse(function, out _), Is.False);
        }

        private static CssMathLiteral Number(double value) => new(value, "", default);
        private static CssMathOperation Unary(string name, CssMathExpression value) => new(name, new[] { value }, value.Type);
    }
}
