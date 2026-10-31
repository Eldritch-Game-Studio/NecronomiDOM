using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    [TestFixture]
    public sealed class ComputedCanonicalRegressionTests
    {
        // CSS Display, Containment, Easing and Images WPT parsing/computed cases.
        [TestCase("contain", "strict", "strict")]
        [TestCase("contain", "content", "content")]
        [TestCase("contain", "style layout paint", "content")]
        [TestCase("contain", "size style layout paint", "strict")]
        [TestCase("display", "flow", "block")]
        [TestCase("display", "block flow", "block")]
        [TestCase("display", "flow block list-item", "list-item")]
        [TestCase("display", "block flow-root list-item", "flow-root list-item")]
        [TestCase("display", "flow inline", "inline")]
        [TestCase("display", "flow-root inline", "inline-block")]
        [TestCase("display", "flex inline", "inline-flex")]
        [TestCase("display", "grid block", "grid")]
        [TestCase("display", "table inline", "inline-table")]
        [TestCase("display", "inline ruby", "ruby")]
        [TestCase("display", "flow inline list-item", "inline list-item")]
        [TestCase("display", "flow run-in list-item", "run-in list-item")]
        [TestCase("background-position-x", "right -10px", "calc(100% + 10px)")]
        [TestCase("background-position-y", "bottom -10px", "calc(100% + 10px)")]
        [TestCase("background-position", "top 17px right -18px", "calc(100% + 18px) 17px")]
        [TestCase("background-position-x", "x-start, x-end", "0%, 100%")]
        [TestCase("background-position-y", "y-start, y-end", "0%, 100%")]
        [TestCase("background-size", "auto", "auto")]
        [TestCase("background-size", "auto auto", "auto")]
        [TestCase("background-size", "auto, 100%", "auto, 100% auto")]
        [TestCase("animation-timing-function", "cubic-bezier(calc(-2), calc(0.7 / 2), calc(1.5), calc(0))", "cubic-bezier(0, 0.35, 1, 0)")]
        [TestCase("animation-timing-function", "steps(calc(-10), start)", "steps(1, start)")]
        [TestCase("animation-timing-function", "steps(calc(5 / 2), start)", "steps(3, start)")]
        [TestCase("animation-timing-function", "steps(calc(1), jump-none)", "steps(2, jump-none)")]
        [TestCase("font-language-override", "\"ENG \"", "\"ENG\"")]
        [TestCase("font-language-override", "\" en \"", "\" en\"")]
        [TestCase("animation-name", "\"something\"", "something")]
        [TestCase("animation-name", "\"multi word string\"", "multi\\ word\\ string")]
        [TestCase("animation-name", "\"none\", \"INITIAL\", \"inherit\"", "\"none\", \"INITIAL\", \"inherit\"")]
        [TestCase("animation-range-start", "cover 0%", "cover")]
        [TestCase("animation-range-end", "exit 1%, cover 2%, contain 100%", "exit 1%, cover 2%, contain")]
        [TestCase("animation-range-start", "cover calc(10% - 10%)", "cover")]
        [TestCase("animation-range-end", "cover calc(50% + 50%), entry calc(99% + 1%)", "cover, entry")]
        [TestCase("letter-spacing", "0px", "normal")]
        [TestCase("text-align-last", "match-parent", "match-parent")]
        [TestCase("rotate", "100 200 300 400grad", "100 200 300 360deg")]
        [TestCase("rotate", "0 0 0 400grad", "0 0 0 360deg")]
        [TestCase("rotate", "x 400grad", "x 360deg")]
        [TestCase("rotate", "0.5 0 0 400grad", "x 360deg")]
        [TestCase("rotate", "0 -0.5 0 400grad", "y -360deg")]
        [TestCase("rotate", "0 0 -0.5 400grad", "-360deg")]
        [TestCase("align-content", "first baseline", "baseline")]
        [TestCase("position-area", "center", "center")]
        [TestCase("position-area", "left", "left")]
        [TestCase("position-area", "inline-start", "inline-start")]
        [TestCase("position-area", "inline-start block-start", "start")]
        [TestCase("position-area", "start start", "start")]
        [TestCase("position-area", "top left", "left top")]
        [TestCase("aspect-ratio", "1", "1 / 1")]
        [TestCase("aspect-ratio", "auto 1.3", "auto 1.3 / 1")]
        [TestCase("overflow-clip-margin", "content-box 0px", "content-box")]
        [TestCase("overflow-clip-margin", "padding-box", "0px")]
        [TestCase("overflow-clip-margin", "padding-box 10px", "10px")]
        [TestCase("scroll-snap-align", "start start", "start")]
        [TestCase("scroll-snap-type", "inline proximity", "inline")]
        [TestCase("filter", "blur() brightness() contrast() grayscale() hue-rotate() invert() opacity() saturate() sepia()", "blur(0px) brightness(1) contrast(1) grayscale(1) hue-rotate(0deg) invert(1) opacity(1) saturate(1) sepia(1)")]
        [TestCase("filter", "invert(2) opacity(calc(300%)) sepia(calc(3)) grayscale(2)", "invert(1) opacity(1) sepia(1) grayscale(1)")]
        [TestCase("justify-items", "legacy", "normal")]
        [TestCase("flex-basis", "calc(10% + 0px)", "10%")]
        [TestCase("background-image", "image-set(url(\"image.png\") calc(-1 * 1x))", "image-set(url(\"image.png\") 0dppx)")]
        [TestCase("background-image", "radial-gradient(at right center, red, blue)", "radial-gradient(at 100% 50%, rgb(255, 0, 0), rgb(0, 0, 255))")]
        [TestCase("background-image", "conic-gradient(at top 5px right 10px, red, blue)", "conic-gradient(at calc(100% - 10px) 5px, rgb(255, 0, 0), rgb(0, 0, 255))")]
        public void MeasuredIndependentCases(string property, string specified, string expected)
        {
            Assert.That(ComputedPropertyTests.Resolve(property, specified, ComputedUnitTests.Context), Is.EqualTo(expected));
        }

        [Test]
        public void CrossPropertyCanonicalizationUsesActualContext()
        {
            var context = ComputedUnitTests.Context with
            {
                PropertyValue = name => name == "position" ? new CssKeywordValue("absolute") : name == "animation-timeline" ? new CssKeywordValue("auto") : null,
                ParentPropertyValue = name => name == "justify-items" ? new CssListValue(new CssValue[] { new CssKeywordValue("legacy"), new CssKeywordValue("left") }, CssListSeparator.Space) : null
            };
            Assert.That(ComputedPropertyTests.Resolve("display", "ruby-base", context), Is.EqualTo("block"));
            Assert.That(ComputedPropertyTests.Resolve("justify-items", "legacy", context), Is.EqualTo("legacy left"));
            Assert.That(ComputedPropertyTests.Resolve("animation-duration", "auto, auto", context), Is.EqualTo("0s, 0s"));
            Assert.That(ComputedPropertyTests.Resolve("animation-duration", "auto", context with { PropertyValue = null }), Is.EqualTo("auto"));
        }

        [Test]
        public void ShadowImplicitColorPreservesComputedLegacySerialization()
        {
            var context = ComputedUnitTests.Context with { CurrentColor = new ComputedValueColor(new("srgb", 0, 0, 1), true) };
            Assert.That(ComputedPropertyTests.Resolve("box-shadow", "1px 2px", context), Is.EqualTo("rgb(0, 0, 255) 1px 2px 0px 0px"));
        }

        [Test]
        public void UnknownCounterBasePreservesNumericExpressionWithoutInventedDefaults()
        {
            var context = new ComputedValueContext();
            Assert.That(ComputedPropertyTests.Resolve("counter-increment", "myCounter calc(10 + (sign(2cqw - 10px) * 5))", context),
                Is.EqualTo("myCounter calc(10 + (5 * sign(2cqw - 10px)))"));
            Assert.That(ComputedPropertyTests.Resolve("width", "0cqw", context), Is.EqualTo("0px"));
            Assert.That(ComputedPropertyTests.Resolve("width", "1cqw", context), Is.EqualTo("1cqw"));
        }

        [Test]
        public void CurrentColorRetainsModernSourceColorSpace()
        {
            var context = ComputedUnitTests.Context with { CurrentColor = ComputedPropertyTests.Parse("color", "color(display-p3 1 0.2 0.3)") };
            Assert.That(ComputedPropertyTests.Resolve("border-top-color", "currentcolor", context), Is.EqualTo("color(display-p3 1 0.2 0.3)"));
            Assert.That(ComputedPropertyTests.Resolve("border-top-color", "alpha(from currentcolor / 0.5)", context), Is.EqualTo("color(display-p3 1 0.2 0.3 / 0.5)"));
        }

        [TestCase("radial-gradient(circle calc(-1px), red, blue)", "radial-gradient(0px, rgb(255, 0, 0), rgb(0, 0, 255))")]
        [TestCase("radial-gradient(ellipse calc(-1px) calc(-2%), red, blue)", "radial-gradient(0px 0%, rgb(255, 0, 0), rgb(0, 0, 255))")]
        [TestCase("repeating-radial-gradient(circle calc(-1px) at -10px 20px, red -5px, blue)", "repeating-radial-gradient(0px at -10px 20px, rgb(255, 0, 0) -5px, rgb(0, 0, 255))")]
        [TestCase("linear-gradient(calc(-10deg), red -5px, blue)", "linear-gradient(-10deg, rgb(255, 0, 0) -5px, rgb(0, 0, 255))")]
        public void RadialGeometryClampsWithoutClampingOffsetsStopsOrOtherGradientAngles(string specified, string expected)
        {
            Assert.That(ComputedPropertyTests.Resolve("background-image", specified, ComputedUnitTests.Context), Is.EqualTo(expected));
        }

        [Test]
        public void SolidAndLightDarkImagesUseActualColorAndSchemeContext()
        {
            var context = ComputedUnitTests.Context;
            Assert.That(ComputedPropertyTests.Resolve("background-image", "image(transparent)", context), Is.EqualTo("image(rgba(0, 0, 0, 0))"));
            Assert.That(ComputedPropertyTests.Resolve("background-image", "light-dark(url(\"light.png\"), none)", context), Is.EqualTo("url(\"light.png\")"));
            Assert.That(ComputedPropertyTests.Resolve("background-image", "light-dark(url(\"light.png\"), none)", context with { DarkColorScheme = true }), Is.EqualTo("image(rgba(0, 0, 0, 0))"));
            Assert.That(ComputedPropertyTests.Resolve("background-image", "light-dark(image(color(display-p3 1 0.2 0.3)), image(red))", context), Is.EqualTo("image(color(display-p3 1 0.2 0.3))"));
        }

        [TestCase("circle(calc(-1px) at 50% -50%)", "circle(0px at 50% -50%)")]
        [TestCase("ellipse(calc(-1px) calc(-2%))", "ellipse(0px 0%)")]
        [TestCase("polygon(round calc(1px - 1px), 1px 2px, 3px 4px)", "polygon(1px 2px, 3px 4px)")]
        [TestCase("polygon(1em 0px, 20px 0px, 20px 20px)", "polygon(20px 0px, 20px 0px, 20px 20px)")]
        [TestCase("inset(1px 2px 3px 4px round calc(-1px) / calc(-2%))", "inset(1px 2px 3px 4px round 0px / 0%)")]
        [TestCase("xywh(0 1% 2px 0.5em)", "inset(1% calc(100% - 2px) calc(99% - 10px) 0px)")]
        [TestCase("xywh(calc(0px) calc(1px + 1%) calc(2px + 2%) calc(3px + 3%))", "inset(calc(1% + 1px) calc(98% - 2px) calc(96% - 4px) 0px)")]
        [TestCase("xywh(0px 1% 2px 3% round 0 1px)", "inset(1% calc(100% - 2px) 96% 0px round 0px 1px)")]
        [TestCase("rect(auto auto auto auto)", "inset(0px)")]
        [TestCase("rect(0px 1% 2px 0.5em)", "inset(0px 99% calc(100% - 2px) 10px)")]
        [TestCase("shape(from 0px 0px, arc to 10px 20px of calc(-1px) calc(-2%) cw)", "shape(from 0px 0px, arc to 10px 20px of 0px 0% cw)")]
        public void BasicShapeComputedRangesAndInsetConversions(string specified, string expected)
        {
            Assert.That(ComputedPropertyTests.Resolve("clip-path", specified, ComputedUnitTests.Context), Is.EqualTo(expected));
        }

        // CSS Shapes 1/2 canonical polygon arguments are immutable after unit normalization.
        [TestCase("polygon(0px 0px, 20px 0px, 20px 20px)")]
        [TestCase("polygon(evenodd, 0px 0px, 20px 0px, 20px 20px)")]
        [TestCase("polygon(round 2px, 0px 0px, 20px 0px, 20px 20px)")]
        public void CanonicalPolygonComputationKeepsItsGroupsWithoutWarmAllocation(string text)
        {
            CssValue value = ComputedPropertyTests.Parse("clip-path", text);
            var context = ComputedUnitTests.Context;
            Assert.That(ComputedValueResolver.Resolve("clip-path", value, context), Is.SameAs(value));
            for (int i = 0; i < 100; i++) _ = ComputedValueResolver.Resolve("clip-path", value, context);
            CssValue last = value;
            long allocated = AllocationWindow.Measure(1000, _ => last = ComputedValueResolver.Resolve("clip-path", value, context));
            Assert.That(last, Is.SameAs(value));
            Assert.That(allocated, Is.Zero);
        }

        // CSS Backgrounds/Borders computed keyword lengths and scalar-radius normalization are context-free.
        [TestCase("thin", "solid", 1)]
        [TestCase("medium", "solid", 3)]
        [TestCase("thick", "solid", 5)]
        [TestCase("medium", "none", 0)]
        public void WarmBorderKeywordComputationAllocatesNoRepeatedNamesOrLengths(string specified, string style, double expected)
        {
            CssValue value = ComputedPropertyTests.Parse("border-left-width", specified);
            var lineStyle = new CssKeywordValue(style);
            var context = ComputedUnitTests.Context with { DevicePixelRatio = 1, PropertyValue = _ => lineStyle };
            for (int i = 0; i < 100; i++) _ = ComputedValueResolver.Resolve("border-left-width", value, context);
            CssValue last = value;
            long allocated = AllocationWindow.Measure(1000, _ => last = ComputedValueResolver.Resolve("border-left-width", value, context));
            Assert.That(((CssDimensionValue)last).Value, Is.EqualTo(expected));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void CanonicalScalarRadiusKeepsItsValueWithoutARepeatedSingletonArray()
        {
            var value = new CssDimensionValue(3, "px");
            for (int i = 0; i < 100; i++) _ = ComputedValueResolver.Resolve("border-top-left-radius", value, default);
            CssValue last = value;
            long allocated = AllocationWindow.Measure(1000, _ => last = ComputedValueResolver.Resolve("border-top-left-radius", value, default));
            Assert.That(last, Is.SameAs(value));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void LayoutDependentShapeSizeClampRetainsItsOriginalSizeSlots()
        {
            Assert.That(ComputedPropertyTests.Resolve("clip-path", "xywh(0px 0px calc(10% - 1px) 1px)", ComputedUnitTests.Context),
                Is.EqualTo("xywh(0px 0px calc(10% - 1px) 1px)"));
            Assert.That(ComputedPropertyTests.Resolve("clip", "rect(1px, 2px, 3px, 4px)", ComputedUnitTests.Context),
                Is.EqualTo("rect(1px, 2px, 3px, 4px)"));
        }

        [Test]
        public void IntegerGrammarBoundariesRoundAndNeverSerializeExponents()
        {
            Assert.That(CssRegistrationSyntax.TryParseValue("<integer>", CssParser.ParseComponentValueList("calc(2.5)"), out CssValue? specified), Is.True);
            Assert.That(ComputedValueResolver.Resolve("--count", specified!, ComputedUnitTests.Context).Serialize(), Is.EqualTo("3"));
            Assert.That(ComputedPropertyTests.Resolve("order", "calc(1e7)", ComputedUnitTests.Context), Is.EqualTo("10000000"));
            Assert.That(ComputedPropertyTests.Resolve("animation-timing-function", "steps(calc(1e7), start)", ComputedUnitTests.Context), Is.EqualTo("steps(10000000, start)"));
            Assert.That(ComputedPropertyTests.Resolve("grid-template-columns", "repeat(calc(1e7), 1px)", ComputedUnitTests.Context), Is.EqualTo("repeat(10000000, 1px)"));
            Assert.That(ComputedPropertyTests.Resolve("grid-template-columns", "repeat(calc(-2), 1px)", ComputedUnitTests.Context), Is.EqualTo("repeat(1, 1px)"));
        }

        [TestCase("-webkit-cross-fade(url(\"from.png\"), url(\"to.png\"), 50%)", "-webkit-cross-fade(url(\"from.png\"), url(\"to.png\"), 0.5)")]
        [TestCase("-webkit-cross-fade(url(\"from.png\"), url(\"to.png\"), calc(50%))", "-webkit-cross-fade(url(\"from.png\"), url(\"to.png\"), 50%)")]
        [TestCase("-webkit-cross-fade(none, url(\"to.png\"), calc(0.25))", "-webkit-cross-fade(none, url(\"to.png\"), 25%)")]
        [TestCase("-webkit-cross-fade(none, url(\"to.png\"), calc(1em / 1px))", "-webkit-cross-fade(none, url(\"to.png\"), 100%)")]
        public void CrossFadeMathProgressMatchesChromePercentageSerialization(string specified, string expected)
        {
            CssValue value = ComputedValueResolver.Resolve("background-image", ComputedPropertyTests.Parse("background-image", specified), ComputedUnitTests.Context);
            Assert.That(value.Serialize(), Is.EqualTo(expected));
            Assert.That(ComputedValueResolver.Resolve("background-image", value, ComputedUnitTests.Context).Serialize(), Is.EqualTo(expected));
        }
        [Test]
        public void CrossFadeMixedNumberPercentageMathMatchesChromeRejection()
        {
            // Chrome 155: the specified declaration is dropped and the computed background-image remains none.
            Assert.That(CssPropertyParser.Parse("background-image", CssParser.ParseComponentValueList(
                "-webkit-cross-fade(url(\"from.png\"), url(\"to.png\"), calc(0.25 + 25%))")), Is.Null);
        }
    }
}
