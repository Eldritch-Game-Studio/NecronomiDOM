using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Colors
{
    [TestFixture]
    public sealed class CssColorMeasuredRegressionTests
    {
        // WPT color-valid-hwb.html and color-computed-hwb.html; Chrome 155 passes.
        [TestCase("hwb(120 30% 50%)","rgb(77, 128, 77)")]
        [TestCase("hwb(120 30 50)","rgb(77, 128, 77)")]
        [TestCase("hwb(120 30% 50)","rgb(77, 128, 77)")]
        [TestCase("hwb(120 30 50%)","rgb(77, 128, 77)")]
        [TestCase("hwb(120 30% 50% / .5)","rgba(77, 128, 77, 0.5)")]
        [TestCase("hwb(120 30% 50% / 0)","rgba(77, 128, 77, 0)")]
        public void HwbByteQuantizationRoundsArithmeticMidpointsUp(string text,string expected)
        {
            CssColorValue value=Parse(text);
            Assert.That(value.Serialize(),Is.EqualTo(expected));
            Assert.That(value.TryResolve(new CssColorContext(),out CssResolvedColor color),Is.True);
            Assert.That(color.SerializeComputed(value.IsLegacyRgb),Is.EqualTo(expected));
        }
        [Test]
        public void ByteRoundingDoesNotMoveARealValueBelowTheMidpoint()
        {
            var color=new CssResolvedColor("srgb",(127.5-0.00000001)/255,.5,.3);
            Assert.That(color.SerializeComputed(true),Is.EqualTo("rgb(127, 128, 77)"));
        }
        // Color 4 §4.4.1 HWB noise removal and WPT color-computed-powerless.html.
        [TestCase("hsl(from hwb(180 49.999% 50%) h s l)","hsl",.49999)]
        [TestCase("hsl(from hwb(180 49.999% 50%) h calc(s * 1000) l)","hsl",.49999)]
        [TestCase("color-mix(in hsl, hwb(180 49.999% 50%))","hsl",.49999)]
        [TestCase("hwb(from hsl(from hwb(180 99.999% none) h s l) h w b)","hwb",0)]
        [TestCase("hwb(from hsl(from hwb(180 none 99.999%) h s l) h w b)","hwb",1)]
        public void HwbPowerlessConversionRemovesResidualChromaIncludingMissingComponents(string text,string space,double third)
        {
            CssResolvedColor color=Resolve(text);
            Assert.That(color.Space,Is.EqualTo(space));
            Assert.That(double.IsNaN(color.C1),Is.True);
            Assert.That(color.C3,Is.EqualTo(third).Within(0.000000001));
            Assert.That(color.C2,Is.EqualTo(space=="hsl" ? 0 : 1-third).Within(0.000000001));
        }
        [Test]
        public void ChromaAboveTheHwbEpsilonStillContributesToRelativeMath()
        {
            CssResolvedColor color=Resolve("hsl(from hwb(180 49.998% 50%) h calc(s * 1000) l)");
            AssertCoordinates(color,"srgb",.48999,.50999,.50999,0.00001);
        }
        [TestCase("color-mix(in hsl, hwb(180 49.999% 50%), hsl(11 33 44))",.54754418,.42088052,.39244583)]
        [TestCase("color-mix(in hwb, hsl(from hwb(180 99.999% none) h s l), hwb(11 33 44))",.78,.68608333,.665)]
        [TestCase("color-mix(in hwb, hsl(from hwb(180 none 99.999%) h s l), hwb(11 33 44))",.28,.18608333,.165)]
        public void ConvertedPowerlessHueCarriesTheOtherMixHue(string text,double r,double g,double b)
        { AssertCoordinates(Resolve(text),"srgb",r,g,b,0.00001); }
        // Color 4 sample conversions.js: exact primaries, Bradford adaptation and consistent XYZ/LMS white.
        // WPT color-computed-powerless.html hue tolerances expose coefficient truncation.
        [TestCase("lch(from hwb(180 99.998% 0%) l c h)","lch",99.99960858,.00185022,199.69899791)]
        [TestCase("lch(from hwb(180 49.998% 50%) l c h)","lch",53.38851967,.00210384,199.69888976)]
        [TestCase("lch(from color(display-p3 .18936885 .18937816 .18937561) l c h)","lch",19.99999994,.00151062,180.02568063)]
        [TestCase("oklch(from hwb(180 99.998% 0%) l c h)","oklch",.99999614,.00000541,197.15830446)]
        [TestCase("oklch(from lch(20 .00151 180) l c h)","oklch",.31034434,.00000446,179.50900432)]
        [TestCase("oklch(from color(display-p3 .0860989 .08610658 .08610454) l c h)","oklch",.2,.0000041,179.99967542)]
        public void NearAchromaticConversionRetainsReferenceHuePrecision(string text,string space,double l,double c,double h)
        { AssertCoordinates(Resolve(text),space,l,c,h,0.001); }
        [TestCase("lch(100% 0 0)")]
        [TestCase("oklch(100% 0 0)")]
        public void NeutralWhiteDoesNotProduceLargeHslSaturationByDividingRoundoff(string white)
        {
            AssertCoordinates(Resolve("color-mix(in hsl, "+white+", blue)"),"srgb",.625,.625,.875,0.00001);
        }
        // CSS Color 4 Rec.2020 display EOTF is signed gamma 2.4 (BT.1886).
        // These WPT expectations differ from Chrome 155's older camera OETF.
        [TestCase("color(from color(rec2020 .25 .5 .75) srgb r g b)","srgb",-.328686,.491201,.76185)]
        [TestCase("color(from color(srgb .25 .5 .75) rec2020 r g b)","rec2020",.420493,.518024,.741381)]
        public void Rec2020UsesDisplayTransferIncludingExtendedNegativeCoordinates(string text,string space,double r,double g,double b)
        { AssertCoordinates(Resolve(text),space,r,g,b,0.00001); } // Same rounded-coordinate precision as WPT color expectations.
        // Color 5 §3.3 uses progress .5 for each zero-total pair, not invented equal n-way weights.
        // Chrome 155 rejects the three-color syntax; this follows the exact WPT case instead.
        [Test]
        public void ZeroTotalThreeColorMixUsesSequentialHalfInterpolationThenZeroAlpha()
        {
            CssResolvedColor color=Resolve("color-mix(in srgb, red 0%, green 0%, blue 0%)");
            AssertCoordinates(color,"srgb",.25,.12549,.5,0.000001);
            Assert.That(color.Alpha,Is.EqualTo(0));
        }
        [Test]
        public void ContainerDependentColorMathNeedsActualNumericMetrics()
        {
            CssColorValue value=Parse("color(srgb calc(.5 + sign(2cqw - 10px) * .1) 0 0 / .51)");
            Assert.That(value.TryResolve(new CssColorContext(),out _),Is.False);
            // The channel reader accepts host-supplied units; it does not invent a container width.
            var context=new CssColorContext { NumericContext=new CssMathEvaluationContext(unit=>unit=="cqw" ? 8 : null) };
            Assert.That(value.TryResolve(context,out CssResolvedColor color),Is.True);
            Assert.That(color.C1,Is.EqualTo(.6).Within(0.000000001));
        }
        [Test]
        public void BareConversionsRetainExtendedPerceptualLightness()
        {
            // Color 4 §11.1 explicitly forbids clamping conversion results, even negative lightness.
            CssResolvedColor color=new CssResolvedColor("oklch",0,.2,0).ConvertTo("lch");
            Assert.That(color.C1,Is.LessThan(0));
        }
        private static void AssertCoordinates(CssResolvedColor color,string space,double first,double second,double third,double tolerance)
        {
            Assert.That(color.Space,Is.EqualTo(space));
            Assert.That(color.C1,Is.EqualTo(first).Within(tolerance));
            Assert.That(color.C2,Is.EqualTo(second).Within(tolerance));
            Assert.That(color.C3,Is.EqualTo(third).Within(tolerance));
        }
        private static CssResolvedColor Resolve(string text)
        {
            Assert.That(Parse(text).TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            return result;
        }
        private static CssColorValue Parse(string text)
        {
            Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out CssValue? value),Is.True);
            return (CssColorValue)value!;
        }
    }
}
