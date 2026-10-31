using System;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    // Geometry Interfaces 1 sections 6.3-6.6. WPT cd1d8aa5bfebc15d1af2af230c7ff95f9e582a4c:
    // css/geometry/DOMMatrix-003.html and DOMMatrix-invert-{invertible,non-invertible}.html.
    [TestFixture]
    public sealed class GeometryMatrixTests
    {
        [Test]
        public void CssPostMultiplicationLeavesExistingTranslationOutsideScaleAndFlip()
        {
            var matrix = DOMMatrix.Identity.Translate(10, 20);
            Point(matrix.Scale(2, 3).TransformPoint(new DOMPoint(1, 2)), 12, 26, 0, 1);
            Point(matrix.FlipX().TransformPoint(new DOMPoint(1, 2)), 9, 22, 0, 1);
            Point(matrix.FlipY().TransformPoint(new DOMPoint(1, 2)), 11, 18, 0, 1);
            Point(matrix.TransformPoint(new DOMPoint(1, 2)), 11, 22, 0, 1);
            Point(DOMMatrix.Identity.Scale(2).Translate(10, 20).TransformPoint(), 20, 40, 0, 1);
        }

        [Test]
        public void ScaleOriginsApplyBeforeTheExistingMatrix()
        {
            var matrix = DOMMatrix.Identity.Translate(10, 20, 30);
            var scaled = matrix.Scale(2, 5, 3, 11, 7, 13);
            Matrix(scaled, matrix.Translate(11, 7, 13).Scale(2, 5, 3).Translate(-11, -7, -13));
            Point(scaled.TransformPoint(new DOMPoint(11, 7, 13)), 21, 27, 43, 1);
            Point(scaled.TransformPoint(new DOMPoint(12, 9, 17)), 23, 37, 55, 1);
            matrix.ScaleSelf(2, 5, 3, 11, 7, 13);
            Matrix(matrix, scaled);
        }

        [Test]
        public void UniformThreeDimensionalScaleUsesEveryOriginAndLegacyScaleDefaultsYToOne()
        {
            var matrix = DOMMatrix.Identity.Translate(3, 4, 5);
            Matrix(matrix.Scale3d(7, 5, 2, 3), matrix.Translate(5, 2, 3).Scale(7, 7, 7).Translate(-5, -2, -3));
            Point(matrix.ScaleNonUniform(6).TransformPoint(new DOMPoint(1, 2, 3)), 9, 6, 8, 1);
            var expected = matrix.Scale3d(7, 5, 2, 3);
            matrix.Scale3dSelf(7, 5, 2, 3);
            Matrix(matrix, expected);
            Assert.That(DOMMatrix.Identity.Scale(1, 1, 1, 1, 1, 1).Is2D, Is.False);
        }

        [Test]
        public void RotationHasSingleAngleOverloadAndSpecifiedZThenYThenXOrder()
        {
            var matrix = DOMMatrix.Identity.Translate(5, 7, 11).Scale(2, 3, 4);
            Matrix(matrix.Rotate(90), matrix.RotateAxisAngle(0, 0, 1, 90));
            Matrix(matrix.Rotate(25, 35, 45), matrix.RotateAxisAngle(0, 0, 1, 45)
                .RotateAxisAngle(0, 1, 0, 35).RotateAxisAngle(1, 0, 0, 25));
            Point(DOMMatrix.Identity.Rotate(90).TransformPoint(new DOMPoint(1)), 0, 1, 0, 1);
            Matrix(matrix.Rotate(25, 0), matrix.RotateAxisAngle(1, 0, 0, 25));
            Matrix(matrix.Rotate(25, null, 0), matrix.RotateAxisAngle(1, 0, 0, 25));
            var expected = matrix.Rotate(25, 35, 45);
            matrix.RotateSelf(25, 35, 45);
            Matrix(matrix, expected);
            Assert.That(DOMMatrix.Identity.Rotate(360, 0, 0).Is2D, Is.False);
        }

        [Test]
        public void VectorRotationAndSkewsUseCssDirectionAndDegrees()
        {
            var matrix = DOMMatrix.Identity.Translate(5, 7);
            Matrix(matrix.RotateFromVector(1, 1), matrix.Rotate(45));
            Matrix(matrix.RotateFromVector(0, 1), matrix.Rotate(90));
            Matrix(matrix.RotateFromVector(0, 0), matrix);
            Matrix(matrix.RotateFromVector(-0.0, -0.0), matrix);
            var expected = matrix.RotateFromVector(-1, 0);
            var mutable = matrix;
            mutable.RotateFromVectorSelf(-1, 0);
            Matrix(mutable, expected);
            Point(matrix.SkewX(45).TransformPoint(new DOMPoint(1, 2)), 8, 9, 0, 1);
            Point(matrix.SkewY(45).TransformPoint(new DOMPoint(1, 2)), 6, 10, 0, 1);
            mutable = matrix; mutable.SkewXSelf(45); Matrix(mutable, matrix.SkewX(45));
            mutable = matrix; mutable.SkewYSelf(45); Matrix(mutable, matrix.SkewY(45));
        }

        [Test]
        public void InverseReversesComplexCompositionAndDoesNotMutateItsSource()
        {
            var matrix = DOMMatrix.Identity.RotateAxisAngle(0, 0, 1, -30).Scale(10, -0.5, 2.5)
                .RotateAxisAngle(0, 1, 0, 10).Translate(10, -20.5, 30.5).RotateAxisAngle(1, 0, 0, 20);
            var snapshot = matrix;
            var expected = DOMMatrix.Identity.RotateAxisAngle(1, 0, 0, -20).Translate(-10, 20.5, -30.5)
                .RotateAxisAngle(0, 1, 0, -10).Scale(0.1, -2, 0.4).RotateAxisAngle(0, 0, 1, 30);
            var inverse = matrix.Inverse();
            Matrix(inverse, expected);
            Matrix(matrix, snapshot);
            Matrix(matrix.Multiply(inverse), DOMMatrix.Identity, compareDimension: false);
            matrix.InvertSelf();
            Matrix(matrix, inverse);
        }

        [Test]
        public void InverseUsesPivotingAndDoublePrecisionWithoutSingularityEpsilon()
        {
            var swapAxes = new DOMMatrix(new double[] { 0, 1, 1, 0, 0, 0 });
            Matrix(swapAxes.Inverse(), swapAxes);
            var perspective = DOMMatrix.Identity.Translate(3, 5, 7).Rotate(17, 23, 31).Scale(2, 3, 4);
            perspective.M14 = 0.01; perspective.M24 = -0.02; perspective.M34 = 0.03;
            Matrix(perspective.Multiply(perspective.Inverse()), DOMMatrix.Identity, compareDimension: false);
            var tiny = DOMMatrix.Identity.Scale(1e-200, 1e-200, 1e-200);
            var inverse = tiny.Inverse();
            Assert.That(inverse.M11 / 1e200, Is.EqualTo(1).Within(1e-14));
            Assert.That(inverse.M22 / 1e200, Is.EqualTo(1).Within(1e-14));
            Assert.That(inverse.M33 / 1e200, Is.EqualTo(1).Within(1e-14));
            var precise = DOMMatrix.Identity.Translate(16777217, 0).Scale(1.00000000000001);
            Assert.That(precise.M41, Is.EqualTo(16777217));
            Assert.That(precise.Inverse().M11, Is.EqualTo(1 / precise.M11));
        }

        [Test]
        public void SingularInverseSetsAllSixteenEntriesToNaNAndCannotStringify()
        {
            var matrix = DOMMatrix.Identity;
            matrix.A = 0;
            var inverse = matrix.Inverse();
            Assert.That(matrix.A, Is.Zero);
            Assert.That(inverse.Is2D, Is.False);
            Assert.That(inverse.IsIdentity, Is.False);
            foreach (double value in inverse.ToFloat64Array()) Assert.That(double.IsNaN(value), Is.True);
            foreach (float value in inverse.ToFloat32Array()) Assert.That(float.IsNaN(value), Is.True);
            matrix.InvertSelf();
            foreach (double value in matrix.ToFloat64Array()) Assert.That(double.IsNaN(value), Is.True);
            Assert.That(Assert.Throws<DomException>(() => inverse.ToString())!.Name, Is.EqualTo("InvalidStateError"));
            var singular3D = DOMMatrix.Identity; singular3D.M44 = 0;
            foreach (double value in singular3D.Inverse().ToFloat64Array()) Assert.That(double.IsNaN(value), Is.True);
        }

        [Test]
        public void DimensionStateDoesNotReturnToTwoDimensionsAfterThreeDimensionalHistory()
        {
            var identity3D = DOMMatrix.FromFloat64Array(DOMMatrix.Identity.ToFloat64Array());
            Assert.That(identity3D.IsIdentity, Is.True);
            Assert.That(identity3D.Is2D, Is.False);
            Assert.That(identity3D.Inverse().Is2D, Is.False);
            Assert.That(DOMMatrix.Identity.Multiply(identity3D).Is2D, Is.False);
            var matrix = DOMMatrix.Identity; matrix.M13 = 2; matrix.M13 = 0;
            Assert.That(matrix.Is2D, Is.False);
            Assert.That(DOMMatrix.Identity.Scale(4, -0.5).Inverse().Is2D, Is.True);
        }

        [Test]
        public void TwoDimensionalScaleAndRotationSweepsKeepTheirExactEmbeddingAfterInverse()
        {
            // Same ranges as WPT DOMMatrix-invert-preserves-2d.html.
            for (double scale = 0.1; scale < 20; scale += 0.1)
                Check(DOMMatrix.Identity.Scale(scale, scale));
            for (double angle = 0; angle < 180; angle += 2)
                Check(DOMMatrix.Identity.Rotate(angle));

            static void Check(DOMMatrix matrix)
            {
                Assert.That(matrix.Is2D, Is.True);
                var inverse = matrix.Inverse();
                Assert.That(inverse.Is2D, Is.True);
                Assert.That(inverse.M13, Is.Zero); Assert.That(inverse.M14, Is.Zero);
                Assert.That(inverse.M23, Is.Zero); Assert.That(inverse.M24, Is.Zero);
                Assert.That(inverse.M31, Is.Zero); Assert.That(inverse.M32, Is.Zero);
                Assert.That(inverse.M33, Is.EqualTo(1)); Assert.That(inverse.M34, Is.Zero);
                Assert.That(inverse.M43, Is.Zero); Assert.That(inverse.M44, Is.EqualTo(1));
                matrix.InvertSelf();
                Matrix(matrix, inverse);
            }
        }

        [Test]
        public void WarmNativeGeometryOperationsAllocateNothing()
        {
            var matrix = DOMMatrix.Identity.Translate(3, 5, 7).Scale(2, 3, 4);
            var point = new DOMPoint(1, 2, 3, 2);
            DOMPoint output = new();
            var affine = DOMMatrix.Identity.Translate(3, 5).Scale(2, 3);
            for (int i = 0; i < 128; i++) Exercise();
            long allocated = AllocationWindow.Measure(256, _ => Exercise());
            Assert.That(allocated, Is.Zero);
            Assert.That(double.IsFinite(output.X), Is.True);

            void Exercise()
            {
                var result = matrix.Inverse().Rotate(10, 20, 30).Scale3d(2, 3, 5, 7).SkewX(10).SkewY(20).FlipX().FlipY();
                result.InvertSelf(); result.Scale3dSelf(0.5); result.RotateSelf(5);
                output = result.TransformPoint(point);
                output = output.MatrixTransform(affine.Inverse());
            }
        }

        private static void Matrix(DOMMatrix actual, DOMMatrix expected, bool compareDimension = true)
        {
            var a = actual.ToFloat64Array(); var e = expected.ToFloat64Array();
            for (int i = 0; i < 16; i++) Assert.That(a[i], Is.EqualTo(e[i]).Within(1e-10), $"matrix entry {i}");
            if (compareDimension) Assert.That(actual.Is2D, Is.EqualTo(expected.Is2D));
        }

        private static void Point(DOMPoint point, double x, double y, double z, double w)
        {
            Assert.That(point.X, Is.EqualTo(x).Within(1e-10));
            Assert.That(point.Y, Is.EqualTo(y).Within(1e-10));
            Assert.That(point.Z, Is.EqualTo(z).Within(1e-10));
            Assert.That(point.W, Is.EqualTo(w).Within(1e-10));
        }
    }
}
