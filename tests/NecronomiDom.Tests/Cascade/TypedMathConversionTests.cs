using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    [TestFixture]
    public sealed class TypedMathConversionTests
    {
        // Typed OM numeric conversion and sum-value equality use the existing Values math engine.
        [Test]
        public void ConversionAndEqualityUseCanonicalUnitsAndRetainRelativeSymbols()
        {
            var sum = new CSSMathSum(new CSSUnitValue(1, "in"), new CSSUnitValue(96, "px"));
            Assert.That(sum.To("px").Value, Is.EqualTo(192));
            Assert.That(sum.Equals(new CSSUnitValue(2, "in")), Is.True);
            var relative = new CSSMathSum(new CSSUnitValue(2, "em"), new CSSUnitValue(3, "em"));
            Assert.That(relative.To("em").Value, Is.EqualTo(5));
            Assert.That(relative.Equals(new CSSUnitValue(5, "em")), Is.True);
            Assert.Throws<DomTypeError>(() => relative.To("px"));
            Assert.Throws<DomTypeError>(() => new CSSMathSum(new CSSUnitValue(1, "em"), new CSSUnitValue(1, "rem")).To("em"));
        }
        [Test]
        public void ProductsCancelDimensionsAndUnaryValuesEvaluateThroughMathCore()
        {
            var product = new CSSMathProduct(new CSSUnitValue(2, "em"), new CSSUnitValue(3, "number"));
            Assert.That(product.To("em").Value, Is.EqualTo(6));
            Assert.That(new CSSMathNegate(new CSSUnitValue(2, "em")).To("em").Value, Is.EqualTo(-2));
            Assert.That(new CSSMathInvert(2).To("number").Value, Is.EqualTo(0.5));
            var cancelled = new CSSMathProduct(new CSSUnitValue(2, "px"), new CSSMathInvert(new CSSUnitValue(1, "px")));
            Assert.That(cancelled.Type.IsNumber, Is.True);
            Assert.That(cancelled.To("number").Value, Is.EqualTo(2));
            Assert.That(new CSSMathInvert(new CSSMathInvert(new CSSUnitValue(2, "em"))).To("em").Value, Is.EqualTo(2));
        }
        [Test]
        public void MinMaxClampUseSharedEvaluationAndLowerWinsWhenBoundsCross()
        {
            Assert.That(new CSSMathMin(3, 1, 2).To("number").Value, Is.EqualTo(1));
            Assert.That(new CSSMathMax(3, 1, 2).To("number").Value, Is.EqualTo(3));
            Assert.That(new CSSMathClamp(1, 2, 3).To("number").Value, Is.EqualTo(2));
            Assert.That(new CSSMathClamp(1, -2, 3).To("number").Value, Is.EqualTo(1));
            Assert.That(new CSSMathClamp(1, 5, 3).To("number").Value, Is.EqualTo(3));
            Assert.That(new CSSMathClamp(10, 5, 1).To("number").Value, Is.EqualTo(10));
            Assert.That(new CSSMathMin(new CSSUnitValue(1, "in"), new CSSUnitValue(100, "px")).To("px").Value, Is.EqualTo(96));
        }
        [Test]
        public void ConstructedNumericTreesCanBeWrittenToTheRealStyleMap()
        {
            var document = new Document().Implementation.CreateHTMLDocument("typed-math");
            _ = document.DefaultView;
            var element = (HTMLElement)document.CreateElement("div");
            document.Body!.AppendChild(element);
            CSSNumericValue sum = new CSSMathSum(new CSSUnitValue(2, "px"), new CSSUnitValue(3, "px"));
            element.AttributeStyleMap.Set("width", sum);
            Assert.That(CSSNumericValue.Parse(element.Style.GetPropertyValue("width")).To("px").Value, Is.EqualTo(5));
            CSSNumericValue min = new CSSMathMin(new CSSUnitValue(4, "px"), new CSSUnitValue(5, "px"));
            element.AttributeStyleMap.Set("width", min);
            Assert.That(CSSNumericValue.Parse(element.Style.GetPropertyValue("width")).To("px").Value, Is.EqualTo(4));
            CSSNumericValue clamp = new CSSMathClamp(new CSSUnitValue(1, "px"), new CSSUnitValue(2, "px"), new CSSUnitValue(3, "px"));
            element.AttributeStyleMap.Set("width", clamp);
            Assert.That(CSSNumericValue.Parse(element.Style.GetPropertyValue("width")).To("px").Value, Is.EqualTo(2));
            CSSNumericValue angle = new CSSMathNegate(new CSSUnitValue(30, "deg"));
            Assert.That(angle.To("deg").Value, Is.EqualTo(-30));
        }
    }
}
