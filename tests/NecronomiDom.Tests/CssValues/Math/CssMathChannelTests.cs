using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathChannelTests
    {
        [TestCase("calc(r / 2)", "calc(0.5 * r)")]
        [TestCase("calc(r * (2 + 3))", "calc(5 * r)")]
        [TestCase("calc(R + 1)", "calc(1 + r)")]
        [TestCase("calc(r + g * 2)", "calc(r + (2 * g))")]
        [TestCase("min(r, 1 + 2)", "min(r, 3)")]
        [TestCase("calc(b * .5 - g * .5)", "calc((0.5 * b) - (0.5 * g))")]
        [TestCase("calc(r - g)", "calc(r - g)")]
        [TestCase("calc(r + g * -.5)", "calc(r - (0.5 * g))")]
        [TestCase("calc(r + g * -2 * b)", "calc(r - (2 * g * b))")]
        public void PreservesChannelSymbolsDuringSpecifiedSimplification(string input, string expected)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue(input)!;
            Assert.That(CssMathParser.TryParseChannels(function, new[] { "r", "g", "b", "alpha" }, out CssValue? value), Is.True);
            Assert.That(value!.Serialize(), Is.EqualTo(expected));
            Assert.That(((CssMathValue)value).TryGetConstant(out _), Is.False);
        }

        [Test]
        public void EvaluatesSymbolsOnlyThroughExplicitContext()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(r / 2 + g)")!;
            Assert.That(CssMathParser.TryParseChannels(function, new[] { "r", "g" }, out CssValue? value), Is.True);
            var context = new CssMathEvaluationContext(resolveNumericValue: leaf => leaf is CssKeywordValue keyword
                ? keyword.Value switch { "r" => 120, "g" => 20, _ => null } : null);
            Assert.That(((CssMathValue)value!).TryEvaluate(context, out double result), Is.True);
            Assert.That(result, Is.EqualTo(80));
            Assert.That(value.Serialize(), Is.EqualTo("calc((0.5 * r) + g)"));
        }

        [Test]
        public void SymbolicSubtractionSerializationPreservesEvaluationAndRoundTrips()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(b * .5 - g * .5)")!;
            Assert.That(CssMathParser.TryParseChannels(function, new[] { "b", "g" }, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            var context = new CssMathEvaluationContext(resolveNumericValue: leaf => leaf is CssKeywordValue keyword
                ? keyword.Value switch { "b" => 60, "g" => 20, _ => null } : null);
            Assert.That(math.TryEvaluate(context, out double result), Is.True);
            Assert.That(result, Is.EqualTo(20));
            var serialized = (FunctionValue)CssParser.ParseComponentValue(math.Serialize())!;
            Assert.That(CssMathParser.TryParseChannels(serialized, new[] { "b", "g" }, out CssValue? reparsed), Is.True);
            Assert.That(((CssMathValue)reparsed!).TryEvaluate(context, out result), Is.True);
            Assert.That(result, Is.EqualTo(20));
            Assert.That(reparsed.Serialize(), Is.EqualTo(math.Serialize()));
        }

        [Test]
        public void RejectsIdentifiersNotRegisteredAsChannels()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(r + unknown)")!;
            Assert.That(CssMathParser.TryParseChannels(function, new[] { "r" }, out _), Is.False);
        }
    }
}
