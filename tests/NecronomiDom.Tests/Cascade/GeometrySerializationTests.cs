using System;
using System.Globalization;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    // Literal expectations from css/geometry/DOMMatrix-stringifier.html at WPT
    // cd1d8aa5bfebc15d1af2af230c7ff95f9e582a4c; Geometry Interfaces 1 section 6.5.
    [TestFixture]
    public sealed class GeometrySerializationTests
    {
        [TestCase(1d / 3, "0.3333333333333333")]
        [TestCase(1d / 300000, "0.0000033333333333333333")]
        [TestCase(1d / 300000000, "3.3333333333333334e-9")]
        [TestCase(100000d + 1d / 3, "100000.33333333333")]
        [TestCase(9007199254740992d, "9007199254740992")]
        [TestCase(9007199254740994d, "9007199254740994")]
        [TestCase(double.MaxValue, "1.7976931348623157e+308")]
        [TestCase(double.Epsilon, "5e-324")]
        public void StringifierUsesTheShortestRoundTripDouble(double value, string expected)
        {
            var matrix = DOMMatrix.Identity; matrix.F = value;
            Assert.That(matrix.ToString(), Is.EqualTo($"matrix(1, 0, 0, 1, 0, {expected})"));
            var matrix3D = DOMMatrix.FromFloat64Array(matrix.ToFloat64Array());
            Assert.That(matrix3D.ToString(), Is.EqualTo($"matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, {expected}, 0, 1)"));
        }

        [TestCase(1e-6, "0.000001")]
        [TestCase(1e-7, "1e-7")]
        [TestCase(1e20, "100000000000000000000")]
        [TestCase(1e21, "1e+21")]
        [TestCase(-1e20, "-100000000000000000000")]
        [TestCase(-1e-7, "-1e-7")]
        [TestCase(-0.0, "0")]
        public void EcmaNotationThresholdsAndSignedZeroAreCultureIndependent(double value, string expected)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var matrix = DOMMatrix.Identity; matrix.F = value;
                Assert.That(matrix.ToString(), Is.EqualTo($"matrix(1, 0, 0, 1, 0, {expected})"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void ThreeDimensionalIdentityStringificationRetainsDimensionHistory()
        {
            Assert.That(DOMMatrix.Identity.ToString(), Is.EqualTo("matrix(1, 0, 0, 1, 0, 0)"));
            var matrix = DOMMatrix.FromFloat64Array(DOMMatrix.Identity.ToFloat64Array());
            Assert.That(matrix.ToString(), Is.EqualTo("matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)"));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void StringifierRejectsNonfiniteValuesInAnyElement(double value)
        {
            for (int entry = 0; entry < 16; entry++)
            {
                var values = DOMMatrix.Identity.ToFloat64Array(); values[entry] = value;
                var matrix = DOMMatrix.FromFloat64Array(values);
                Assert.That(Assert.Throws<DomException>(() => matrix.ToString())!.Name, Is.EqualTo("InvalidStateError"));
            }
        }
    }
}
