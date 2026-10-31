using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Colors
{
    [TestFixture]
    public sealed class CssColorResidualSemanticsTests
    {
        [TestCase("color(srgb calc(.5 + sign(2cqw - 10px) * .1) 0 0 / .51)","color(srgb 0.6 0 0 / 0.51)","color(srgb 0.4 0 0 / 0.51)")]
        [TestCase("rgba(calc(50% + sign(2cqw - 10px) * 10%), 0%, 0%, 51%)","rgba(153, 0, 0, 0.51)","rgba(102, 0, 0, 0.51)")]
        [TestCase("hsla(calc(50deg + sign(2cqw - 10px) * 10deg), 100%, 37.5%, 51%)","rgba(191, 191, 0, 0.51)","rgba(191, 128, 0, 0.51)")]
        [TestCase("hwb(calc(110deg + sign(2cqw - 10px) * 10deg) 30 50 / 51%)","rgba(77, 128, 77, 0.51)","rgba(94, 128, 77, 0.51)")]
        [TestCase("lab(calc(50 + sign(2cqw - 10px) * 10) 30 50 / .51)","lab(60 30 50 / 0.51)","lab(40 30 50 / 0.51)")]
        [TestCase("oklab(calc(.5 + sign(2cqw - 10px) * .1) .3 .5 / .51)","oklab(0.6 0.3 0.5 / 0.51)","oklab(0.4 0.3 0.5 / 0.51)")]
        [TestCase("lch(calc(50 + sign(2cqw - 10px) * 10) 30 50 / .51)","lch(60 30 50 / 0.51)","lch(40 30 50 / 0.51)")]
        [TestCase("oklch(calc(.5 + sign(2cqw - 10px) * .1) .3 50 / .51)","oklch(0.6 0.3 50 / 0.51)","oklch(0.4 0.3 50 / 0.51)")]
        [TestCase("contrast-color(color(srgb calc(1 + sign(2cqw - 10px)) calc(1 + sign(2cqw - 10px)) calc(1 + sign(2cqw - 10px))))","rgb(0, 0, 0)","rgb(255, 255, 255)")]
        public void EveryColorFamilyUsesLiveContainerMetrics(string text,string wide,string narrow)
        {
            double width=1000;
            var context=new ComputedValueContext { ContainerUnitBasis=unit=>unit=="cqw" ? width : null };
            CssColorValue value=Parse(text);
            Assert.That(ComputedValueResolver.Resolve("color",value,context).Serialize(),Is.EqualTo(wide));
            width=100;
            Assert.That(ComputedValueResolver.Resolve("color",value,context).Serialize(),Is.EqualTo(narrow));
        }

        [Test]
        public void RelativeHueMathUsesHostTreeCountingWithoutReplacingChannelSymbols()
        {
            CssColorValue value=Parse("lch(from lch(40 20 45) l c calc(h + 180 * sibling-index()))");
            double index=1;
            var context=new ComputedValueContext
            {
                NumericValue=external=>external is CssFunctionValue { Name:"sibling-index" } ? index : null,
            };
            Assert.That(ComputedValueResolver.Resolve("color",value,context).Serialize(),Is.EqualTo("lch(40 20 225)"));
            index=2;
            Assert.That(ComputedValueResolver.Resolve("color",value,context).Serialize(),Is.EqualTo("lch(40 20 45)"));
        }

        [TestCase("oklch(from color(display-p3 0.08610937692934956 0.08610188202042188 0.08610387292083356) l c h)")]
        [TestCase("color-mix(in oklch, color(display-p3 0.08610937692934956 0.08610188202042188 0.08610387292083356))")]
        [TestCase("oklch(.2 .0000041 359.999999)")]
        public void HueJustBelowOneTurnSurvivesSerializationAndReparse(string text)
        {
            CssResolvedColor color=Resolve(text);
            Assert.That(color.C3,Is.GreaterThan(359.99).And.LessThan(360));
            string serialized=color.SerializeComputed();
            CssResolvedColor reparsed=Resolve(serialized);
            Assert.That(reparsed.C3,Is.EqualTo(color.C3).Within(0.000000000001));
            Assert.That(reparsed.SerializeComputed(),Is.EqualTo(serialized));
        }

        [TestCase("oklch(.5 .1 .000000000000005)",.000000000000005)]
        [TestCase("oklch(.5 .1 360)",0)]
        [TestCase("oklch(.5 .1 -360)",0)]
        [TestCase("oklch(.5 .1 -0.000000000000005)",0)]
        public void HueNormalizationStaysWithinHalfOpenTurnWithoutLosingSmallPositiveAngles(string text,double expected)
        {
            Assert.That(Resolve(text).C3,Is.EqualTo(expected));
        }

        [TestCase("color-mix(in srgb, red 50%, blue 50%)","color-mix(in srgb, red, blue)",1)]
        [TestCase("color-mix(in srgb, red 75%, blue 75%)","color-mix(in srgb, red 75%, blue 75%)",1)]
        [TestCase("color-mix(in srgb, red 25%, blue 25%)","color-mix(in srgb, red 25%, blue 25%)",.5)]
        public void EqualMixWeightsOnlyOmitPercentagesWhenTheirTotalIsOneHundred(string text,string specified,double alpha)
        {
            CssColorValue value=Parse(text);
            Assert.That(value.Serialize(),Is.EqualTo(specified));
            Assert.That(value.TryResolve(new CssColorContext(),out CssResolvedColor result),Is.True);
            Assert.That(result,Is.EqualTo(new CssResolvedColor("srgb",.5,0,.5,alpha)));
        }

        [TestCase("alpha(from red / .125)","color(srgb 1 0 0 / 0.125)")]
        [TestCase("alpha(from hsl(0 100% 50%) / .125)","color(srgb 1 0 0 / 0.125)")]
        [TestCase("alpha(from hwb(0 0% 0%) / .125)","color(srgb 1 0 0 / 0.125)")]
        [TestCase("alpha(from red / 2)","color(srgb 1 0 0)")]
        [TestCase("alpha(from red / -1)","color(srgb 1 0 0 / 0)")]
        [TestCase("alpha(from color(display-p3 .1 .2 .3) / .125)","color(display-p3 0.1 0.2 0.3 / 0.125)")]
        public void AlphaModifierKeepsUnderlyingModernSpaceAndUnquantizedAlpha(string text,string computed)
        {
            Assert.That(ComputedValueResolver.Resolve("color",Parse(text),default).Serialize(),Is.EqualTo(computed));
        }

        [Test]
        public void SystemColorRolesResolveThroughAuthoritativeCustomPalette()
        {
            var palette=CssSystemColorPalette.Dark with
            {
                ButtonFace=new("display-p3",.1,.2,.3), ButtonBorder=new("srgb",.3,.4,.5),
                CanvasText=new("srgb",.6,.7,.8),
            };
            var context=new CssColorContext { SystemColor=palette.Resolve };
            Assert.That(Parse("ButtonFace").TryResolve(context,out CssResolvedColor face),Is.True);
            Assert.That(face,Is.EqualTo(palette.ButtonFace));
            Assert.That(Parse("ThreeDShadow").TryResolve(context,out CssResolvedColor border),Is.True);
            Assert.That(border,Is.EqualTo(palette.ButtonBorder));
            Assert.That(Parse("WindowText").TryResolve(context,out CssResolvedColor text),Is.True);
            Assert.That(text,Is.EqualTo(palette.CanvasText));
            Assert.That(Parse("Field").TryResolve(new CssColorContext { SystemColor=_=>null },out _),Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultSchemePaletteDistinguishesTextFromCanvasAndSuppliesConcreteRoles(bool dark)
        {
            CssSystemColorPalette palette=dark ? CssSystemColorPalette.Dark : CssSystemColorPalette.Light;
            var context=new ComputedValueContext { DarkColorScheme=dark, SystemColor=palette.Resolve };
            foreach (string keyword in new[] { "CanvasText", "LinkText", "ActiveText", "VisitedText" })
            {
                CssValue value=ComputedValueResolver.Resolve("color",Parse(keyword),context);
                Assert.That(((CssColorValue)value).TryResolve(new CssColorContext(),out CssResolvedColor text),Is.True);
                Assert.That(text,Is.Not.EqualTo(palette.Canvas));
            }
            Assert.That(ComputedValueResolver.Resolve("color",Parse("light-dark(ButtonFace, Field)"),context).Serialize(),
                Is.EqualTo((dark ? palette.Field : palette.ButtonFace).SerializeComputed(true)));
        }

        [Test]
        public void CurrentColorModifiersResolveTheActualInheritedOrElementColor()
        {
            var context=new ComputedValueContext
            {
                InheritedColor=Parse("blue"), CurrentColor=Parse("red"),
                SystemColor=CssSystemColorPalette.Light.Resolve,
            };
            Assert.That(ComputedValueResolver.Resolve("color",Parse("alpha(from currentcolor / .125)"),context).Serialize(),
                Is.EqualTo("color(srgb 0 0 1 / 0.125)"));
            Assert.That(ComputedValueResolver.Resolve("background-color",Parse("alpha(from currentcolor / .125)"),context).Serialize(),
                Is.EqualTo("color(srgb 1 0 0 / 0.125)"));
            Assert.That(ComputedValueResolver.Resolve("background-color",Parse("contrast-color(currentcolor)"),
                context with { CurrentColor=Parse("white") }).Serialize(),Is.EqualTo("rgb(0, 0, 0)"));
            Assert.That(ComputedValueResolver.Resolve("background-color",Parse("contrast-color(ButtonFace)"),context).Serialize(),
                Is.EqualTo("rgb(0, 0, 0)"));
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
