using System.Collections.Generic;
using NecronomiDom.Css.TypedOm;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    [TestFixture]
    public sealed class TypedMathSerializationTests
    {
        // Exact assertions from WPT css/css-typed-om/stylevalue-serialization/cssMathValue.tentative.html.
        public static IEnumerable<TestCaseData> Cases()
        {
            yield return Case(new CSSMathMax(1), "max(1)");
            yield return Case(new CSSMathMax(1, 2, 3), "max(1, 2, 3)");
            yield return Case(new CSSMathMax(Px(100), Px(110)), "max(100px, 110px)");
            yield return Case(new CSSMathMax(new CSSMathSum(1, 2), Number(3)), "max(1 + 2, 3)");
            yield return Case(new CSSMathMin(1), "min(1)");
            yield return Case(new CSSMathMin(1, 2, 3), "min(1, 2, 3)");
            yield return Case(new CSSMathMin(Px(90), Px(100)), "min(90px, 100px)");
            yield return Case(new CSSMathMin(new CSSMathSum(1, 2), Number(3)), "min(1 + 2, 3)");
            yield return Case(new CSSMathClamp(1, 2, 3), "clamp(1, 2, 3)");
            yield return Case(new CSSMathClamp(Px(90), Px(100), Px(110)), "clamp(90px, 100px, 110px)");
            yield return Case(new CSSMathClamp(new CSSMathSum(1, 2), Number(3), Number(4)), "clamp(1 + 2, 3, 4)");
            yield return Case(new CSSMathSum(1), "calc(1)");
            yield return Case(new CSSMathSum(1, 2, 3), "calc(1 + 2 + 3)");
            yield return Case(new CSSMathSum(new CSSMathNegate(1), Number(2), Number(3)), "calc((-1) + 2 + 3)");
            yield return Case(new CSSMathSum(Number(1), new CSSMathNegate(2), Number(3)), "calc(1 - 2 + 3)");
            yield return Case(new CSSMathSum(new CSSMathSum(1, 2), Number(3)), "calc((1 + 2) + 3)");
            yield return Case(new CSSMathNegate(1), "calc(-1)");
            yield return Case(new CSSMathProduct(new CSSMathNegate(1)), "calc((-1))");
            yield return Case(new CSSMathProduct(1), "calc(1)");
            yield return Case(new CSSMathProduct(1, 2, 3), "calc(1 * 2 * 3)");
            yield return Case(new CSSMathProduct(new CSSMathInvert(1), Number(2), Number(3)), "calc((1 / 1) * 2 * 3)");
            yield return Case(new CSSMathProduct(Number(1), new CSSMathInvert(2), Number(3)), "calc(1 / 2 * 3)");
            yield return Case(new CSSMathProduct(new CSSMathProduct(1, 2), Number(3)), "calc((1 * 2) * 3)");
            yield return Case(new CSSMathInvert(1), "calc(1 / 1)");
            yield return Case(new CSSMathSum(new CSSMathInvert(1)), "calc((1 / 1))");
        }
        private static TestCaseData Case(object wrapper, string expected) => new(wrapper, expected);
        private static CSSNumericValue Px(double value) => new CSSUnitValue(value, "px");
        private static CSSNumericValue Number(double value) => new CSSUnitValue(value, "number");
        [TestCaseSource(nameof(Cases))]
        public void NativeConstructorsRetainUpstreamSerialization(object wrapper, string expected)
            => Assert.That(wrapper.ToString(), Is.EqualTo(expected));

        [Test]
        public void NumericProjectionRetainsFunctionAndUnarySourceKinds()
        {
            CSSNumericValue min = new CSSMathMin(1, 2);
            CSSNumericValue max = new CSSMathMax(1, 2);
            CSSNumericValue clamp = new CSSMathClamp(1, 2, 3);
            CSSNumericValue negate = new CSSMathNegate(1);
            CSSNumericValue invert = new CSSMathInvert(2);
            Assert.That(min.ToString(), Is.EqualTo("min(1, 2)"));
            Assert.That(max.ToString(), Is.EqualTo("max(1, 2)"));
            Assert.That(clamp.ToString(), Is.EqualTo("clamp(1, 2, 3)"));
            Assert.That(negate.ToString(), Is.EqualTo("calc(-1)"));
            Assert.That(invert.ToString(), Is.EqualTo("calc(1 / 2)"));
        }
    }
}
