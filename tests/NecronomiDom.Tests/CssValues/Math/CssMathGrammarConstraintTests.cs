using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathGrammarConstraintTests
    {
        [Test]
        public void GrammarConstraintsAreImmutableAndDoNotCoerceSpecifiedValues()
        {
            CssMathValue original = CssMathParserTests.Parse("calc(1.6)");
            CssMathValue constrained = original.WithGrammarConstraints(true, 0, 2);
            Assert.That(original.IsIntegerTarget, Is.False);
            Assert.That(original.GrammarMinimum, Is.EqualTo(double.NegativeInfinity));
            Assert.That(original.GrammarMaximum, Is.EqualTo(double.PositiveInfinity));
            Assert.That(constrained.Root, Is.SameAs(original.Root));
            Assert.That(constrained.Type, Is.EqualTo(original.Type));
            Assert.That(constrained.WasCalc, Is.EqualTo(original.WasCalc));
            Assert.That(constrained.IsIntegerTarget, Is.True);
            Assert.That(constrained.GrammarMinimum, Is.EqualTo(0));
            Assert.That(constrained.GrammarMaximum, Is.EqualTo(2));
            Assert.That(constrained.Serialize(), Is.EqualTo("calc(1.6)"));
            Assert.That(constrained.TryGetConstant(out double value), Is.True);
            Assert.That(value, Is.EqualTo(1.6));
            Assert.That(constrained.WithGrammarConstraints(true, 0, 2), Is.SameAs(constrained));
        }

        [Test]
        public void CanonicalRangeMetadataSurvivesUnitSimplification()
        {
            CssMathValue original = CssMathParserTests.Parse("calc(2em + 1px)").WithGrammarConstraints(false, 0, 50);
            CssMathValue computed = original.Simplify(new CssMathEvaluationContext(unit => unit == "em" ? 16 : null));
            Assert.That(computed.Serialize(), Is.EqualTo("calc(33px)"));
            Assert.That(computed.GrammarMinimum, Is.EqualTo(0));
            Assert.That(computed.GrammarMaximum, Is.EqualTo(50));
            Assert.That(computed.IsIntegerTarget, Is.False);
            Assert.That(computed.Type, Is.EqualTo(original.Type));
        }

        [Test]
        public void DynamicIntegerRangeMetadataIsPreservedForTheComputedConsumer()
        {
            CssMathValue original = CssMathParserTests.Parse("calc(2 * sibling-index())")
                .WithGrammarConstraints(true, 1, 1_000_000);
            CssMathValue unresolved = original.Simplify(new CssMathEvaluationContext());
            Assert.That(unresolved.TryGetConstant(out _), Is.False);
            Assert.That(unresolved.IsIntegerTarget, Is.True);
            Assert.That(unresolved.GrammarMinimum, Is.EqualTo(1));
            Assert.That(unresolved.GrammarMaximum, Is.EqualTo(1_000_000));
            var context = new CssMathEvaluationContext(resolveNumericValue: value =>
                value is CssFunctionValue { Name: "sibling-index", Arguments: null } ? 10_000_000 : null);
            CssMathValue computed = original.Simplify(context);
            Assert.That(computed.IsIntegerTarget, Is.True);
            Assert.That(computed.GrammarMinimum, Is.EqualTo(1));
            Assert.That(computed.GrammarMaximum, Is.EqualTo(1_000_000));
            Assert.That(computed.TryGetConstant(out double result), Is.True);
            Assert.That(result, Is.EqualTo(20_000_000));
        }
    }
}
