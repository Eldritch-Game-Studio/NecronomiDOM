using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathTreeCountingTests
    {
        [TestCase("sibling-index()", "sibling-index", 3)]
        [TestCase("sibling-count( )", "sibling-count", 5)]
        [TestCase("SIBLING-INDEX()", "sibling-index", 3)]
        public void TreeCountingFunctionsAreStrictTypedNumericLeaves(string input, string name, double expected)
        {
            CssMathValue math = CssMathParserTests.Parse(input);
            Assert.That(CssMathParser.IsMathFunction(name), Is.True);
            Assert.That(math.Fits("number"), Is.True);
            Assert.That(math.Fits("integer"), Is.True);
            Assert.That(math.Serialize(), Is.EqualTo(name + "()"));
            Assert.That(math.Root, Is.TypeOf<CssMathNumericLeaf>());
            Assert.That(((CssMathNumericLeaf)math.Root).Value, Is.TypeOf<CssFunctionValue>());
            Assert.That(math.TryGetConstant(out _), Is.False);
            var context = new CssMathEvaluationContext(resolveNumericValue: value =>
                value is CssFunctionValue function && function.Name == name && function.Arguments == null ? expected : null);
            Assert.That(math.TryEvaluate(context, out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(math.Simplify(context).TryGetConstant(out actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ArithmeticUsesSuppliedTreeDataWithoutInventingIt()
        {
            CssMathValue math = CssMathParserTests.Parse("calc(2 * sibling-index() + sibling-count())");
            Assert.That(math.Fits("integer"), Is.True);
            Assert.That(math.TryEvaluate(new CssMathEvaluationContext(), out _), Is.False);
            var context = new CssMathEvaluationContext(resolveNumericValue: value => value switch
            {
                CssFunctionValue { Name: "sibling-index", Arguments: null } => 3,
                CssFunctionValue { Name: "sibling-count", Arguments: null } => 5,
                _ => null
            });
            Assert.That(math.TryEvaluate(context, out double result), Is.True);
            Assert.That(result, Is.EqualTo(11));
            Assert.That(math.Simplify(context).Serialize(), Is.EqualTo("calc(11)"));
        }

        [TestCase("sibling-index(1)")]
        [TestCase("sibling-count(auto)")]
        [TestCase("sibling-count(,)")]
        [TestCase("calc(sibling-index(1) + 1)")]
        [TestCase("sibling-indices()")]
        [TestCase("sibling-\u017Findex()")]
        public void TreeCountingRejectsArgumentsAndUnknownFunctions(string input)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue(input)!;
            Assert.That(CssMathParser.TryParse(function, out _), Is.False);
        }
    }
}
