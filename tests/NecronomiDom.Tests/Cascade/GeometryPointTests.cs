using System;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    // Geometry Interfaces 1 sections 2 and 6.3/6.5. WPT cd1d8aa5bfebc15d1af2af230c7ff95f9e582a4c:
    // css/geometry/DOMPoint-002.html, DOMPoint-matrixTransform-translation-w.html, DOMMatrix-003.html.
    [TestFixture]
    public sealed class GeometryPointTests
    {
        [Test]
        public void PointConstructorsAndFromPointUseHomogeneousDefaults()
        {
            Point(new DOMPoint(), 0, 0, 0, 1);
            Point(new DOMPoint(1), 1, 0, 0, 1);
            Point(new DOMPoint(1, 2), 1, 2, 0, 1);
            Point(new DOMPoint(1, 2, 3), 1, 2, 3, 1);
            Point(DOMPoint.FromPoint(), 0, 0, 0, 1);
            Assert.That(new DOMPointReadOnly().W, Is.EqualTo(1));
            Assert.That(DOMPointReadOnly.FromPoint().W, Is.EqualTo(1));
            var mutable = new DOMPoint(1, 2, 3, 4);
            DOMPointReadOnly snapshot = mutable;
            mutable.X = 99; mutable.W = 0;
            Point(DOMPoint.FromPoint(snapshot), 1, 2, 3, 4);
            Point(snapshot.MatrixTransform(), 1, 2, 3, 4);
            mutable.X = double.NaN; mutable.Y = double.PositiveInfinity;
            mutable.Z = double.NegativeInfinity; mutable.W = -0.0;
            Assert.That(double.IsNaN(mutable.X), Is.True);
            Assert.That(mutable.Y, Is.EqualTo(double.PositiveInfinity));
            Assert.That(mutable.Z, Is.EqualTo(double.NegativeInfinity));
            Assert.That(BitConverter.DoubleToInt64Bits(mutable.W), Is.EqualTo(long.MinValue));
        }

        [Test]
        public void TranslationScalesByWAndDoesNotTranslateDirections()
        {
            var matrix = DOMMatrix.Identity.Translate(10, 20, 30);
            var point = new DOMPoint(1, 2, 3, 2);
            Point(point.MatrixTransform(matrix), 21, 42, 63, 2);
            Point(matrix.TransformPoint(point), 21, 42, 63, 2);
            Point(point, 1, 2, 3, 2);
            Point(matrix.TransformPoint(new DOMPoint(1, 2, 3, 0)), 1, 2, 3, 0);
            Point(DOMMatrix.Identity.TransformPoint(new DOMPoint(1, 2, 3, 5)), 1, 2, 3, 5);
            Point(matrix.TransformPoint(), 10, 20, 30, 1);
        }

        [Test]
        public void PointTransformationUsesCssColumnMajorEntriesWithoutPerspectiveDivision()
        {
            var point = new DOMPointReadOnly(1, 2, 3, 4);
            var affine = new DOMMatrix(new double[] { 1, 2, 3, 4, 5, 6 });
            Point(affine.TransformPoint(point), 27, 34, 3, 4);
            var matrix = DOMMatrix.FromFloat64Array(new double[]
                { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            Point(matrix.TransformPoint(point), 90, 100, 110, 120);
            Point(point.MatrixTransform(matrix), 90, 100, 110, 120);
            var example = new DOMPoint(5, 4);
            Point(example.MatrixTransform(new DOMMatrix(new double[] { 2, 0, 0, 2, 10, 10 })), 20, 18, 0, 1);
        }

        [Test]
        public void StandardArrayFactoriesCopySixOrSixteenEntriesAndExportsAreFresh()
        {
            var six = new double[] { 1, 2, 3, 4, 5, 6 };
            var matrix = DOMMatrix.FromFloat64Array(six);
            Assert.That(matrix.Is2D, Is.True);
            six[0] = 99;
            Assert.That(matrix.M11, Is.EqualTo(1));
            var expected = new double[] { 1, 2, 0, 0, 3, 4, 0, 0, 0, 0, 1, 0, 5, 6, 0, 1 };
            Assert.That(matrix.ToFloat64Array(), Is.EqualTo(expected));
            var export = matrix.ToFloat64Array(); export[0] = 77;
            Assert.That(matrix.M11, Is.EqualTo(1));
            var floats = new float[] { 1, 2, 3, 4, 5, 6 };
            Assert.That(DOMMatrix.FromFloat32Array(floats).ToFloat64Array(), Is.EqualTo(expected));
            var sixteen = matrix.ToFloat32Array();
            var imported = DOMMatrix.FromFloat32Array(sixteen);
            Assert.That(imported.Is2D, Is.False);
            Assert.That(imported.ToFloat32Array(), Is.EqualTo(sixteen));
            var doubles = new double[] { 1.00000000000001, 2, 3, 4, 5, 6 };
            Assert.That(DOMMatrix.FromFloat64Array(doubles).M11, Is.EqualTo(doubles[0]));
            Assert.That(DOMMatrix.FromFloat64Array(doubles).ToFloat32Array()[0], Is.EqualTo(1f));
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(7)]
        [TestCase(15)]
        [TestCase(17)]
        public void ArrayFactoriesRejectEveryNonstandardLength(int length)
        {
            Assert.Throws<DomTypeError>(() => DOMMatrix.FromFloat32Array(new float[length]));
            Assert.Throws<DomTypeError>(() => DOMMatrix.FromFloat64Array(new double[length]));
        }

        [Test]
        public void ArrayFactoriesPreserveNonfiniteNumbersAndSignedZero()
        {
            var matrix = DOMMatrix.FromFloat64Array(new double[] { double.NaN, -0.0, double.PositiveInfinity, 1, 0, 0 });
            var doubles = matrix.ToFloat64Array(); var floats = matrix.ToFloat32Array();
            Assert.That(double.IsNaN(doubles[0]), Is.True);
            Assert.That(float.IsNaN(floats[0]), Is.True);
            Assert.That(BitConverter.DoubleToInt64Bits(doubles[1]), Is.EqualTo(long.MinValue));
            Assert.That(BitConverter.SingleToInt32Bits(floats[1]), Is.EqualTo(int.MinValue));
            Assert.That(doubles[4], Is.EqualTo(double.PositiveInfinity));
            Assert.That(floats[4], Is.EqualTo(float.PositiveInfinity));
            Assert.That(Assert.Throws<DomException>(() => matrix.ToString())!.Name, Is.EqualTo("InvalidStateError"));
        }

        [Test]
        public void ToJsonContainsStandardMembersAndPreservesNonfiniteNumericValues()
        {
            var point = new DOMPoint(1, 2, double.NaN, 4);
            var json = point.ToJson();
            Assert.That(json.Count, Is.EqualTo(4));
            Assert.That(json["x"], Is.EqualTo(1));
            Assert.That(json["y"], Is.EqualTo(2));
            Assert.That(double.IsNaN(json["z"]), Is.True);
            Assert.That(json["w"], Is.EqualTo(4));
            DOMPointReadOnly readOnly = point;
            Assert.That(readOnly.ToJson(), Is.EqualTo(json));
            point.X = 9;
            Assert.That(json["x"], Is.EqualTo(1));
            var matrix = DOMMatrix.Identity.Translate(3, 5);
            var matrixJson = matrix.ToJson();
            Assert.That(matrixJson.Count, Is.EqualTo(24));
            Assert.That(matrixJson["a"], Is.EqualTo(matrixJson["m11"]));
            Assert.That(matrixJson["f"], Is.EqualTo(matrixJson["m42"]));
            Assert.That(matrixJson["is2D"], Is.EqualTo(true));
            Assert.That(matrixJson["isIdentity"], Is.EqualTo(false));
            Assert.That(matrixJson["m44"], Is.EqualTo(1d));
            matrix.M41 = 9;
            Assert.That(matrixJson["e"], Is.EqualTo(3d));
            Assert.That((double)DOMMatrix.Identity.Scale(0).Inverse().ToJson()["m11"], Is.NaN);
        }

        [Test]
        public void WarmArrayImportsDoNotAllocateIntermediateStorage()
        {
            var doubles = DOMMatrix.Identity.ToFloat64Array();
            var floats = DOMMatrix.Identity.ToFloat32Array();
            DOMMatrix result = new();
            for (int i = 0; i < 128; i++) { result = DOMMatrix.FromFloat64Array(doubles); result = DOMMatrix.FromFloat32Array(floats); }
            long allocated = AllocationWindow.Measure(256, _ => { result = DOMMatrix.FromFloat64Array(doubles); result = DOMMatrix.FromFloat32Array(floats); });
            Assert.That(allocated, Is.Zero);
            Assert.That(result.IsIdentity, Is.True);
        }

        private static void Point(DOMPoint point, double x, double y, double z, double w)
        {
            Assert.That(point.X, Is.EqualTo(x)); Assert.That(point.Y, Is.EqualTo(y));
            Assert.That(point.Z, Is.EqualTo(z)); Assert.That(point.W, Is.EqualTo(w));
        }
    }
}
