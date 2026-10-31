using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Colors
{
    [TestFixture]
    public sealed class CssColorModificationTests
    {
        // Color 5/WPT alpha-color-computed.html uses modern serialization; Chrome 155
        // still serializes the older rgba result.
        [TestCase("alpha(from red / .5)","alpha(from red / 0.5)","color(srgb 1 0 0 / 0.5)")]
        [TestCase("contrast-color(white)","contrast-color(white)","rgb(0, 0, 0)")]
        [TestCase("color(display-p3-linear .1 .2 .3)","color(display-p3-linear 0.1 0.2 0.3)","color(display-p3-linear 0.1 0.2 0.3)")]
        public void SpecifiedAndComputedModificationValues(string input,string specified,string computed)
        {
            CssColorValue value=Parse(input);
            Assert.That(value.Serialize(),Is.EqualTo(specified));
            Assert.That(value.TryResolve(new CssColorContext(),out CssResolvedColor resolved),Is.True);
            Assert.That(resolved.SerializeComputed(value.IsLegacyRgb),Is.EqualTo(computed));
        }
        [TestCase("alpha(red / .5)")]
        // Chrome 155 passes all five WPT alpha-color-parsing-invalid.html omission cases.
        [TestCase("alpha(from red)")]
        [TestCase("alpha(from currentcolor)")]
        [TestCase("alpha(from rgba(255, 0, 0, 0.3))")]
        [TestCase("alpha(from alpha(from currentcolor / 0.5))")]
        [TestCase("alpha(from alpha(from currentcolor) / 0.5)")]
        [TestCase("alpha(from red .5)")]
        [TestCase("alpha(from red / r)")]
        [TestCase("alpha(from red / 10px)")]
        [TestCase("alpha(from red / mystery(1))")]
        [TestCase("contrast-color(red, blue)")]
        [TestCase("contrast-color(garbage)")]
        [TestCase("color(display-p3-linear 1 2)")]
        public void RejectsInvalidModificationGrammar(string input)
        { Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(input),out _),Is.False); }
        [Test]
        public void AlphaMathReadsOriginAlphaAndKeepsItsColorSpace()
        {
            CssColorValue value=Parse("alpha(from color(display-p3 .1 .2 .3 / .8) / calc(alpha / 2))");
            Assert.That(value.TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result,Is.EqualTo(new CssResolvedColor("display-p3",.1,.2,.3,.4)));
        }
        [Test]
        public void ExplicitAlphaInheritsAndNonePreservesMissingAlpha()
        {
            Assert.That(Parse("alpha(from red / alpha)").TryResolve(new CssColorContext(),out CssResolvedColor inherited),Is.True);
            Assert.That(inherited.Alpha,Is.EqualTo(1));
            Assert.That(Parse("alpha(from red / none)").TryResolve(new CssColorContext(),out CssResolvedColor missing),Is.True);
            Assert.That(double.IsNaN(missing.Alpha),Is.True);
        }
        [Test]
        public void ContrastTracksCurrentColorContext()
        {
            CssColorValue value=Parse("contrast-color(currentcolor)");
            Assert.That(value.RequiresCurrentColor,Is.True);
            Assert.That(value.TryResolve(new CssColorContext { CurrentColor=new("srgb",0,0,0) },out CssResolvedColor result),Is.True);
            Assert.That(result,Is.EqualTo(new CssResolvedColor("srgb",1,1,1)));
        }
        [Test]
        public void LinearP3HasTheP3PrimariesWithoutTheGammaTransfer()
        {
            CssResolvedColor linear=new("display-p3-linear",.5,.5,.5);
            CssResolvedColor gamma=linear.ConvertTo("display-p3");
            Assert.That(gamma.C1,Is.EqualTo(.7353569830524495).Within(.000000001));
            Assert.That(gamma.ConvertTo("display-p3-linear").C1,Is.EqualTo(.5).Within(.000000001));
            Assert.That(linear.ConvertTo("xyz-d65").C2,Is.EqualTo(.5).Within(.000000001));
        }
        // Chrome 155 inline-style serialization.
        [TestCase("rgb(calc(50% + .25) 0 0)")]
        [TestCase("rgb(calc(50% + 20) 0 0)")]
        [TestCase("lab(50 calc(20% + .25) 0)")]
        [TestCase("lch(50 calc(20% + .25) 0)")]
        [TestCase("oklab(.5 calc(20% + .25) 0)")]
        [TestCase("color(srgb calc(50% + .25) 0 0)")]
        [TestCase("rgb(from red calc(r + 50%) g b)")]
        [TestCase("alpha(from red / calc(alpha - 25%))")]
        public void RejectsMixedNumberPercentageChannelMath(string text)
        { Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out _),Is.False); }
        [TestCase("rgb(sign(0% - 0px) 0 0)")]
        [TestCase("rgb(calc(0% + 1px) 0 0)")]
        public void ColorNumericMathRejectsLengthPercentageHints(string text)
        { Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out _),Is.False); }
        private static CssColorValue Parse(string text)
        {
            Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out CssValue? value),Is.True);
            return (CssColorValue)value!;
        }
    }
}
