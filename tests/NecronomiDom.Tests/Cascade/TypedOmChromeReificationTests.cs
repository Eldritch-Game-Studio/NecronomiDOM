using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    public sealed class TypedOmChromeReificationTests
    {
        // All reification and transform expectations below match Chrome 155.
        [TestCase("1px", "CSSUnitValue", "1px")]
        [TestCase("calc(1px)", "CSSMathSum", "calc(1px)")]
        [TestCase("calc(1px + 1px)", "CSSMathSum", "calc(2px)")]
        [TestCase("calc(1px * 2)", "CSSMathSum", "calc(2px)")]
        [TestCase("calc(1px / 2)", "CSSMathSum", "calc(0.5px)")]
        [TestCase("min(1px, 2px)", "CSSUnitValue", "1px")]
        [TestCase("max(1px, 2px)", "CSSUnitValue", "2px")]
        [TestCase("clamp(1px, 2px, 3px)", "CSSUnitValue", "2px")]
        public void NumericParseReifiesTheChromeConstructor(string text, string constructor, string serialized)
        {
            var value = CSSNumericValue.Parse(text).Reify();
            Assert.That(value.GetType().Name, Is.EqualTo(constructor));
            Assert.That(value.ToString(), Is.EqualTo(serialized));
        }
        [Test]
        public void NumericParseRejectsAnExpressionThatHasNoTypedOmRepresentation()
            => Assert.Throws<DomException>(() => CSSNumericValue.Parse("sin(1rad)"));
        [TestCase("translateX(1px)", "translate(1px, 0px)", true)]
        [TestCase("scale(50%)", "scale(0.5, 0.5)", true)]
        [TestCase("rotateX(1deg)", "rotate3d(1, 0, 0, 1deg)", false)]
        [TestCase("perspective(none)", "perspective(none)", false)]
        public void TransformParseReifiesTheChromeComponent(string text, string serialized, bool is2D)
        {
            var value = CSSStyleValue.Parse("transform", text);
            Assert.That(value.TryGetTransform(out var transform), Is.True);
            Assert.That(transform.ToString(), Is.EqualTo(serialized));
            Assert.That(transform.Is2D, Is.EqualTo(is2D));
        }
        [Test]
        public void ThreeDimensionalComponentCanProduceANumericallyTwoDimensionalMatrix()
        {
            var translate = CSSTransformComponent.Translate(new CSSUnitValue(1, "px"), new CSSUnitValue(2, "px"), new CSSUnitValue(0, "px"));
            Assert.That(translate.Is2D, Is.False);
            Assert.That(translate.ToMatrix().Is2D, Is.True);
            Assert.That(CSSTransformComponent.Perspective("none").ToMatrix().Is2D, Is.True);
        }
        [Test]
        public void MatrixDictionaryFixupPreservesAliasesAndExplicitDimensions()
        {
            var matrix = DOMMatrix.FromMatrix(new DOMMatrixInit { A = 2, M11 = 2, F = 7 });
            Assert.That(matrix.M11, Is.EqualTo(2));
            Assert.That(matrix.M42, Is.EqualTo(7));
            Assert.That(matrix.M33, Is.EqualTo(1));
            Assert.That(matrix.Is2D, Is.True);
            Assert.That(DOMMatrix.FromMatrix(new DOMMatrixInit { Is2D = false }).Is2D, Is.False);
            Assert.Throws<DomTypeError>(() => DOMMatrix.FromMatrix(new DOMMatrixInit { A = 1, M11 = 2 }));
            Assert.Throws<DomTypeError>(() => DOMMatrix.FromMatrix(new DOMMatrixInit { M13 = 1, Is2D = true }));
            Assert.That(double.IsNaN(DOMMatrix.FromMatrix(new DOMMatrixInit { A = double.NaN, M11 = double.NaN }).M11), Is.True);
        }
        [Test]
        public void ResolvedCssomDoesNotChangeTheComputedTypedOmValueOrInheritance()
        {
            var document = new Document().Implementation.CreateHTMLDocument();
            var parent = (HTMLElement)document.CreateElement("div");
            var child = (HTMLElement)document.CreateElement("span");
            document.Body!.AppendChild(parent); parent.AppendChild(child);
            parent.Style.CssText = "font-size:20px;line-height:1.5;color:red;caret-color:auto;transform:translateX(2px)";
            child.Style.CssText = "font-size:10px";
            Assert.That(document.DefaultView.GetComputedStyle(parent).GetPropertyValue("line-height"), Is.EqualTo("30px"));
            Assert.That(document.DefaultView.GetComputedStyle(child).GetPropertyValue("line-height"), Is.EqualTo("15px"));
            Assert.That(parent.ComputedStyleMap().Get("line-height")!.Value.TryGetUnit(out var number), Is.True);
            Assert.That(number.Unit, Is.EqualTo("number")); Assert.That(number.Value, Is.EqualTo(1.5));
            Assert.That(document.DefaultView.GetComputedStyle(parent).GetPropertyValue("caret-color"), Is.EqualTo("rgb(255, 0, 0)"));
            Assert.That(document.DefaultView.GetComputedStyle(parent).GetPropertyValue("transform"), Is.EqualTo("matrix(1, 0, 0, 1, 2, 0)"));
        }
    }
}
