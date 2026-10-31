using System;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NUnit.Framework;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Tests.CssValues.Colors
{
    [TestFixture]
    public sealed class CssColorParserTests
    {
        [TestCase("red", "red")]
        [TestCase("ReBeccAPurple", "rebeccapurple")]
        [TestCase("CanvasText", "canvastext")]
        [TestCase("currentColor", "currentcolor")]
        [TestCase("transparent", "transparent")]
        [TestCase("#f00", "rgb(255, 0, 0)")]
        [TestCase("rgb(100% 0% 0% / 50%)", "rgba(255, 0, 0, 0.5)")]
        [TestCase("hsl(120deg 100% 50%)", "rgb(0, 255, 0)")]
        [TestCase("hwb(240 0% 0%)", "rgb(0, 0, 255)")]
        [TestCase("lab(50% 20% -20% / 50%)", "lab(50 25 -25 / 0.5)")]
        [TestCase("oklab(50% 20% -20%)", "oklab(0.5 0.08 -0.08)")]
        [TestCase("oklch(50% 20% -90deg)", "oklch(0.5 0.08 270)")]
        [TestCase("color(display-p3 1 .2 .3)", "color(display-p3 1 0.2 0.3)")]
        [TestCase("color(xyz .1 .2 .3)", "color(xyz-d65 0.1 0.2 0.3)")]
        [TestCase("hsl(120 100 50)", "rgb(0, 255, 0)")]
        [TestCase("hwb(240 0 0)", "rgb(0, 0, 255)")]
        // Chrome 155: legacy channels are 8-bit CSSOM values.
        [TestCase("hwb(20 20% 30% / 50%)", "rgba(179, 94, 51, 0.5)")]
        [TestCase("rgb(none 0 0)", "color(srgb none 0 0)")]
        [TestCase("hsl(none 20% 30% / none)", "hsl(none 20% 30% / none)")]
        [TestCase("rgb(calc(10 + 20), 20, 30)", "rgb(30, 20, 30)")]
        [TestCase("color-mix(in oklab, teal, peru 40%)", "color-mix(teal 60%, peru 40%)")]
        [TestCase("color-mix(red, green, blue)", "color-mix(red, green, blue)")]
        public void SerializesSpecifiedColors(string input,string expected)
        {
            Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(input),out CssValue? value),Is.True);
            Assert.That(value!.Serialize(),Is.EqualTo(expected));
        }
        [TestCase("not-a-color")]
        [TestCase("#12345")]
        [TestCase("rgb(1, 2 3)")]
        [TestCase("rgb(1 2 3 4)")]
        [TestCase("rgb(1, 20%, 3)")]
        [TestCase("blac\u212a")]
        [TestCase("dar\u212agreen")]
        [TestCase("o\u212alab(.5 0 0)")]
        [TestCase("color-mix(in o\u212alab, red, blue)")]
        [TestCase("rgb(1 2 3 / 4 / 5)")]
        [TestCase("hsl(0, 1, 2)")]
        [TestCase("rgba(1, 2, 3, none)")]
        [TestCase("hwb(0, 0%, 0%)")]
        [TestCase("lab(50 20 30deg)")]
        [TestCase("color(unknown 1 2 3)")]
        [TestCase("color(srgb 1 2 3 4)")]
        [TestCase("color-mix(in srgb longer hue, red, blue)")]
        [TestCase("color-mix(in oklab, red -1%, blue)")]
        [TestCase("light-dark(red, blue, green)")]
        [TestCase("rgb(from red unknown g b)")]
        [TestCase("rgb(from red calc(r + potato) g b)")]
        [TestCase("unknown-color(1 2 3)")]
        public void RejectsInvalidColors(string input) => Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(input),out _),Is.False);

        [TestCase("srgb")][TestCase("srgb-linear")][TestCase("display-p3")][TestCase("a98-rgb")]
        [TestCase("prophoto-rgb")][TestCase("rec2020")][TestCase("xyz-d50")][TestCase("xyz-d65")]
        public void ConvertsAllPredefinedSpaces(string space)
        {
            var original = new CssResolvedColor("srgb",0.2,0.4,0.6,0.7);
            CssResolvedColor roundTrip = original.ConvertTo(space).ToSrgb();
            Assert.That(roundTrip.C1,Is.EqualTo(original.C1).Within(0.00001));
            Assert.That(roundTrip.C2,Is.EqualTo(original.C2).Within(0.00001));
            Assert.That(roundTrip.C3,Is.EqualTo(original.C3).Within(0.00001));
            Assert.That(roundTrip.Alpha,Is.EqualTo(0.7));
        }
        [TestCase("lab")][TestCase("lch")][TestCase("oklab")][TestCase("oklch")][TestCase("hsl")][TestCase("hwb")]
        public void ConvertsPerceptualSpaces(string space) => ConvertsAllPredefinedSpaces(space);

        [Test]
        public void RelativeChannelsUseOriginSpaceAndAlpha()
        {
            CssColorValue color = Parse("rgb(from rgb(100 80 60 / .4) calc(r + 20) g b / alpha)");
            Assert.That(color.TryResolve(new CssColorContext(),out CssResolvedColor resolved),Is.True);
            Assert.That(resolved.C1,Is.EqualTo(120/255d).Within(0.00001));
            Assert.That(resolved.Alpha,Is.EqualTo(0.4));
        }
        [Test]
        public void ContextControlsLightDarkCurrentAndSystemColors()
        {
            var context = new CssColorContext { DarkScheme=true, CurrentColor=new("srgb",0.1,0.2,0.3), SystemColor = name => name=="canvastext" ? new("srgb",1,1,1) : null };
            Assert.That(Parse("light-dark(red, currentcolor)").TryResolve(context,out CssResolvedColor selected),Is.True);
            Assert.That(selected,Is.EqualTo(context.CurrentColor));
            Assert.That(Parse("canvastext").TryResolve(context,out CssResolvedColor system),Is.True);
            Assert.That(system.C1,Is.EqualTo(1));
            Assert.That(Parse("canvastext").TryResolve(new CssColorContext(),out _),Is.False);
        }
        [Test]
        public void MixPremultipliesAlphaAndNormalizesPercentages()
        {
            Assert.That(Parse("color-mix(in srgb, red 20%, transparent 20%)").TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result.C1,Is.EqualTo(1));
            Assert.That(result.Alpha,Is.EqualTo(0.2).Within(0.000001));
        }
        [Test]
        public void OklabReferenceForRedMatchesColorFourSample()
        {
            CssResolvedColor lab = new CssResolvedColor("srgb",1,0,0).ConvertTo("oklab");
            Assert.That(lab.C1,Is.EqualTo(0.627955).Within(0.00001));
            Assert.That(lab.C2,Is.EqualTo(0.224863).Within(0.00001));
            Assert.That(lab.C3,Is.EqualTo(0.125846).Within(0.00001));
        }
        [Test]
        public void RelativeSerializationRetainsSymbolicCalculationsAndOriginChannels()
        {
            // Chrome 155: canonical origin and retained explicit alpha.
            Assert.That(Parse("rgb(from red calc(r / 2) g b / 100%)").Serialize(),
                Is.EqualTo("rgb(from red calc(0.5 * r) g b / 100%)"));
            Assert.That(Parse("hsl(from hsl(210 50% 40% / .8) h s l / 1)").Serialize(),
                Is.EqualTo("hsl(from rgba(51, 102, 153, 0.8) h s l / 1)"));
            // HSL origin, with Chrome 155 CSSOM serialization.
            Assert.That(Parse("hsl(from hsl(0 10% 50%) h s l)").Serialize(),
                Is.EqualTo("hsl(from rgb(140, 115, 115) h s l)"));
        }
        [Test]
        public void RelativeCoordinatesRemainExtendedWhileAlphaClamps()
        {
            Assert.That(Parse("rgb(from red calc(r + 255) g b / 2)").TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result.C1,Is.EqualTo(2));
            Assert.That(result.Alpha,Is.EqualTo(1));
        }
        [Test]
        public void NumericContextResolvesUnitDependentChannelMath()
        {
            CssColorValue value=Parse("rgb(from red calc(r * sign(1em)) g b)");
            Assert.That(value.TryResolve(new CssColorContext(),out _),Is.False);
            Assert.That(value.TryResolve(new CssColorContext { NumericContext=new CssMathEvaluationContext(unit=>unit=="em" ? 16 : null) },out CssResolvedColor result),Is.True);
            Assert.That(result.C1,Is.EqualTo(1));
        }
        [Test]
        public void MissingAnalogousComponentsCarryForwardDuringMixing()
        {
            Assert.That(Parse("color-mix(in srgb, color(srgb none 0 0), color(srgb 1 0 0))").TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result.C1,Is.EqualTo(1));
        }
        [Test]
        public void DefaultOklabSupportsMultipleColorArguments()
        {
            Assert.That(Parse("color-mix(red, green, blue)").TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result.Space,Is.EqualTo("oklab"));
            Assert.That(result.Alpha,Is.EqualTo(1));
        }
        [Test]
        public void MissingAnalogousSetsSurviveColorSpaceConversion()
        {
            Assert.That(Parse("color-mix(in lch, lab(50 none none))").TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(double.IsNaN(result.C2),Is.True);
            Assert.That(double.IsNaN(result.C3),Is.True);
        }
        [Test]
        public void MissingAlphaDoesNotEraseInterpolatedColorChannels()
        {
            Assert.That(Parse("color-mix(in srgb, rgb(255 0 0 / none), rgb(0 0 255 / none))").TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result.C1,Is.EqualTo(0.5));
            Assert.That(result.C3,Is.EqualTo(0.5));
            Assert.That(double.IsNaN(result.Alpha),Is.True);
        }
        [TestCase("hsl",240d,1d,0.5d)]
        [TestCase("hwb",240d,0d,0d)]
        public void NativeSrgbHueConversionsDoNotRoundTripThroughXyz(string space,double first,double second,double third)
        {
            CssResolvedColor result=new CssResolvedColor(space,first,second,third).ToSrgb();
            Assert.That(result.C1,Is.EqualTo(0));
            Assert.That(result.C2,Is.EqualTo(0));
            Assert.That(result.C3,Is.EqualTo(1));
        }
        [Test]
        public void ComputedLegacyColorsRoundChannelsWithoutReducingModernPrecision()
        {
            var color=new CssResolvedColor("srgb",0.3,0.5,0.3);
            Assert.That(color.SerializeComputed(true),Is.EqualTo("rgb(77, 128, 77)"));
            Assert.That(color.SerializeComputed(),Is.EqualTo("color(srgb 0.3 0.5 0.3)"));
        }
        [TestCase("hsl")]
        [TestCase("hwb")]
        public void NativeMissingComputedChannelsKeepTheirFunctionAndPercentageScale(string space)
        {
            var color=new CssResolvedColor(space,double.NaN,0.2,0.3,double.NaN);
            Assert.That(color.SerializeComputed(),Is.EqualTo(space+"(none 20% 30% / none)"));
        }
        [TestCase("hsl(from rgb(0 0 0) h s l)")]
        [TestCase("hsl(from lch(20 .0015 180) h calc(s * 1000) l)")]
        [TestCase("hsl(from oklch(.2 .000004 180) h calc(s * 1000) l)")]
        public void ConversionMakesPowerlessHueMissingAndDoesNotAmplifyChromaNoise(string text)
        {
            Assert.That(Parse(text).TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result.Space,Is.EqualTo("hsl"));
            Assert.That(double.IsNaN(result.C1),Is.True);
            Assert.That(result.C2,Is.EqualTo(0));
            Assert.That(result.SerializeComputed(),Does.StartWith("hsl(none "));
        }
        [TestCase("lab(calc(50 * 3) 0 .5)","lab(calc(150) 0 0.5)")]
        [TestCase("lab(200 calc(50%) .5)","lab(100 calc(50%) 0.5)")]
        [TestCase("color(srgb calc(.5 + 1) calc(.5 - 1) calc(.5))","color(srgb calc(1.5) calc(-0.5) calc(0.5))")]
        public void ModernSpecifiedColorsKeepMathTreesAndClampOnlyLiteralChannels(string text,string expected)
        {
            Assert.That(Parse(text).Serialize(),Is.EqualTo(expected));
        }
        [Test]
        public void InterpolationPreservesExplicitSameSpaceHueButConversionMakesGrayHueMissing()
        {
            Assert.That(Parse("color-mix(in lch, lch(100 0 0), blue)").TryResolve(new CssColorContext(),out CssResolvedColor explicitHue),Is.True);
            Assert.That(Parse("color-mix(in lch, white, blue)").TryResolve(new CssColorContext(),out CssResolvedColor convertedHue),Is.True);
            Assert.That(explicitHue.C3,Is.EqualTo(330.682).Within(.001));
            Assert.That(convertedHue.C3,Is.EqualTo(301.364).Within(.001));
        }
        [Test]
        public void ZeroSumMixUsesEqualColorWeightsAndZeroAlpha()
        {
            // Chrome 155 specified and computed serialization.
            CssColorValue value=Parse("color-mix(in oklab, red 0%, blue 0%)");
            Assert.That(value.Serialize(),Is.EqualTo("color-mix(red 0%, blue 0%)"));
            Assert.That(value.TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result.Space,Is.EqualTo("oklab"));
            Assert.That(result.Alpha,Is.EqualTo(0));
            Assert.That(result.C1,Is.EqualTo(.539974).Within(.0001));
        }
        private static CssColorValue Parse(string text)
        {
            Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out CssValue? value),Is.True);
            return (CssColorValue)value!;
        }
    }
}
