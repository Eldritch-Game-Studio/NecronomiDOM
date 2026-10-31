using System;
using System.Collections;
using System.Collections.Generic;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    [TestFixture]
    public sealed class TypedMathConstructorTests
    {
        // Constructor/type requirements: upstream numeric-objects/cssMathValue.tentative.html,
        // cssMathNegate-type.any.js and cssMathInvert-type.any.js.
        [Test]
        public void EmptyVariadicConstructorsThrowSyntaxError()
        {
            Assert.That(Assert.Throws<DomException>(() => new CSSMathSum())!.Name, Is.EqualTo("SyntaxError"));
            Assert.That(Assert.Throws<DomException>(() => new CSSMathProduct())!.Name, Is.EqualTo("SyntaxError"));
            Assert.That(Assert.Throws<DomException>(() => new CSSMathMin())!.Name, Is.EqualTo("SyntaxError"));
            Assert.That(Assert.Throws<DomException>(() => new CSSMathMax())!.Name, Is.EqualTo("SyntaxError"));
            Assert.Throws<DomTypeError>(() => new CSSMathNegate());
            Assert.Throws<DomTypeError>(() => new CSSMathInvert());
            Assert.Throws<DomTypeError>(() => new CSSMathClamp());
        }
        [Test]
        public void OperatorsAndOperandPropertiesExposeNumericObjects()
        {
            var sum = new CSSMathSum(1, 2, 3, 4, 5);
            var product = new CSSMathProduct(0);
            var min = new CSSMathMin(0);
            var max = new CSSMathMax(0);
            var negate = new CSSMathNegate(0);
            var invert = new CSSMathInvert(0);
            var clamp = new CSSMathClamp(1, 2, 3);
            Assert.That(sum.Operator, Is.EqualTo("sum"));
            Assert.That(product.Operator, Is.EqualTo("product"));
            Assert.That(min.Operator, Is.EqualTo("min"));
            Assert.That(max.Operator, Is.EqualTo("max"));
            Assert.That(negate.Operator, Is.EqualTo("negate"));
            Assert.That(invert.Operator, Is.EqualTo("invert"));
            Assert.That(clamp.Operator, Is.EqualTo("clamp"));
            Assert.That(sum.Values.Length, Is.EqualTo(5));
            for (int i = 0; i < sum.Values.Length; i++)
                Assert.That(sum.Values[i].Equals(new CSSUnitValue(i + 1, "number")), Is.True);
            Assert.That(negate.Value.Equals(new CSSUnitValue(0, "number")), Is.True);
            Assert.That(invert.Value.Equals(new CSSUnitValue(0, "number")), Is.True);
            Assert.That(clamp.Lower.To("number").Value, Is.EqualTo(1));
            Assert.That(clamp.Value.To("number").Value, Is.EqualTo(2));
            Assert.That(clamp.Upper.To("number").Value, Is.EqualTo(3));
        }
        [Test]
        public void AdditiveConstructorsRejectIncompatibleDimensions()
        {
            CSSNumericValue px = new CSSUnitValue(1, "px"), seconds = new CSSUnitValue(1, "s");
            Assert.Throws<DomTypeError>(() => new CSSMathSum(px, seconds));
            Assert.Throws<DomTypeError>(() => new CSSMathMin(px, seconds));
            Assert.Throws<DomTypeError>(() => new CSSMathMax(px, seconds));
            Assert.Throws<DomTypeError>(() => new CSSMathClamp(px, seconds, px));
            Assert.Throws<DomTypeError>(() => new CSSMathClamp(new CSSUnitValue(1, "number"), px, new CSSUnitValue(3, "number")));
        }
        [Test]
        public void MultiplicationAndInversionReuseDimensionExponents()
        {
            var product = new CSSMathProduct(new CSSUnitValue(0, "px"), new CSSUnitValue(0, "s"));
            Assert.That(product.Type.Length, Is.EqualTo(1));
            Assert.That(product.Type.Time, Is.EqualTo(1));
            var inverted = new CSSMathInvert(product);
            Assert.That(inverted.Type.Length, Is.EqualTo(-1));
            Assert.That(inverted.Type.Time, Is.EqualTo(-1));
            Assert.That(new CSSMathInvert(new CSSMathInvert(new CSSUnitValue(0, "px"))).Type,
                Is.EqualTo(CssNumericType.For(CssNumericDimension.Length)));
            Assert.That(new CSSMathInvert(0).Type.IsNumber, Is.True);
            Assert.That(new CSSMathNegate(new CSSUnitValue(0, "px")).Type,
                Is.EqualTo(CssNumericType.For(CssNumericDimension.Length)));
        }
        [Test]
        public void PercentageHintsArePreservedRatherThanAssumingLayoutBasis()
        {
            var value = new CSSMathSum(new CSSUnitValue(1, "px"), new CSSUnitValue(1, "percent"));
            Assert.That(value.Type.Length, Is.EqualTo(1));
            Assert.That(value.Type.PercentageHint, Is.EqualTo(CssNumericDimension.Length));
            Assert.Throws<DomTypeError>(() => value.To("px"));
            Assert.That(new CSSMathSum(new CSSUnitValue(10, "percent"), new CSSUnitValue(20, "percent")).To("percent").Value,
                Is.EqualTo(30));
        }
        [Test]
        public void OperandSequencesOwnSnapshotsAndSupportBothEnumerableContracts()
        {
            CSSNumericValue[] inputs = { new CSSUnitValue(1, "px"), new CSSUnitValue(2, "px") };
            var sequence = new CSSNumericArray(inputs);
            var sum = new CSSMathSum(inputs);
            var product = new CSSMathProduct(inputs);
            var min = new CSSMathMin(inputs);
            var max = new CSSMathMax(inputs);
            inputs[0] = new CSSUnitValue(99, "s");
            Assert.That(sequence[0].To("px").Value, Is.EqualTo(1));
            Assert.That(sum.Values[0].To("px").Value, Is.EqualTo(1));
            Assert.That(product.Values[0].To("px").Value, Is.EqualTo(1));
            Assert.That(min.Values[0].To("px").Value, Is.EqualTo(1));
            Assert.That(max.Values[0].To("px").Value, Is.EqualTo(1));
            var observed = new List<double>();
            foreach (var value in sequence) observed.Add(value.To("px").Value);
            Assert.That(observed, Is.EqualTo(new[] { 1d, 2d }));
            Assert.That(new List<CSSNumericValue>((IEnumerable<CSSNumericValue>)sequence).Count, Is.EqualTo(2));
            int count = 0;
            foreach (CSSNumericValue value in (IEnumerable)sequence) { Assert.That(value.Type.Length, Is.EqualTo(1)); count++; }
            Assert.That(count, Is.EqualTo(2));
            Assert.Throws<IndexOutOfRangeException>(() => { _ = sequence[-1]; });
            Assert.Throws<IndexOutOfRangeException>(() => { _ = sequence[2]; });
            Assert.That(default(CSSNumericArray).Length, Is.Zero);
        }
        [Test]
        public void UnaryAndClampCopyMutableUnitValueInputs()
        {
            var unit = new CSSUnitValue(2, "px");
            var negate = new CSSMathNegate(unit);
            var invert = new CSSMathInvert(unit);
            var clamp = new CSSMathClamp(unit, unit, unit);
            unit.Value = 99;
            Assert.That(negate.Value.To("px").Value, Is.EqualTo(2));
            Assert.That(invert.Value.To("px").Value, Is.EqualTo(2));
            Assert.That(clamp.Value.To("px").Value, Is.EqualTo(2));
        }
    }
}
