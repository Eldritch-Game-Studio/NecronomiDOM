using System;
using System.Linq;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues
{
    [TestFixture]
    public sealed class WptCanonicalRegressionTests
    {
        // Exact parsing expectations from WPT cd1d8aa5bfebc15d1af2af230c7ff95f9e582a4c.
        // flow-* feature membership additionally follows Chrome 155.
        [TestCase("align-content", "first baseline", "baseline")]
        [TestCase("align-content", "flow-start", "flow-start")]
        [TestCase("justify-content", "safe flow-start", "safe flow-start")]
        [TestCase("background-size", "1px", "1px auto")]
        [TestCase("background-size", "1px auto", "1px auto")]
        [TestCase("mask-size", "1px auto", "1px auto")]
        [TestCase("grid-column-start", "span 1 i", "span i")]
        [TestCase("grid-row-start", "span 2", "span 2")]
        [TestCase("grid-auto-flow", "dense row", "dense")]
        [TestCase("grid-template-areas", "\"  a  \t  b  \"", "\"a b\"")]
        [TestCase("grid-template-areas", "\"first ...\"", "\"first .\"")]
        [TestCase("grid-template-columns", "[] 150px [] 1fr []", "150px 1fr")]
        [TestCase("grid-template-columns", "repeat(1, [] 10px [])", "repeat(1, 10px)")]
        [TestCase("border-image-outset", "0", "0")]
        [TestCase("border-image-outset", "0px", "0px")]
        [TestCase("border-image-width", "1 1 1 1", "1")]
        [TestCase("border-image-slice", "fill 1 1 1 1", "1 fill")]
        [TestCase("display", "inline flow-root", "inline-block")]
        [TestCase("display", "block flow list-item", "list-item")]
        [TestCase("contain", "paint layout style size", "size layout style paint")]
        [TestCase("font-language-override", "\"ENG \"", "\"ENG\"")]
        [TestCase("font-language-override", "\" en \"", "\" en\"")]
        [TestCase("font-size-adjust", "ex-height 0.5", "0.5")]
        [TestCase("font-style", "oblique 0deg", "normal")]
        [TestCase("font-palette", "--", "--")]
        [TestCase("list-style-type", "symbols(symbolic \"a\")", "symbols(\"a\")")]
        [TestCase("text-box-edge", "cap text", "cap")]
        [TestCase("text-box-edge", "text alphabetic", "alphabetic")]
        [TestCase("scroll-snap-align", "start start", "start")]
        [TestCase("scroll-snap-type", "x proximity", "x")]
        [TestCase("aspect-ratio", "16", "16 / 1")]
        [TestCase("aspect-ratio", "16 auto", "auto 16 / 1")]
        [TestCase("counter-increment", "first -1 second third 99", "first -1 second 1 third 99")]
        [TestCase("counter-reset", "chapter reversed(chapter)", "chapter 0 reversed(chapter)")]
        [TestCase("scale", "1%", "0.01")]
        [TestCase("scale", "100% 100% 1", "1")]
        [TestCase("scale", "100% 200% 300%", "1 2 3")]
        [TestCase("translate", "100px 0px 0px", "100px")]
        [TestCase("translate", "1px 2px 0", "1px 2px")]
        [TestCase("rotate", "0.5 0 0 400grad", "x 400grad")]
        [TestCase("rotate", "0 -0.5 0 400grad", "y -400grad")]
        [TestCase("rotate", "0 0 -1 400grad", "-400grad")]
        [TestCase("rotate", "400grad z", "400grad")]
        [TestCase("rotate", "100 200 300 400grad", "100 200 300 400grad")]
        [TestCase("rotate", "0 0 0 400grad", "0 0 0 400grad")]
        [TestCase("perspective-origin", "10%", "10% center")]
        [TestCase("transform-origin", "top", "center top")]
        [TestCase("transform", "scaleX(50%)", "scaleX(0.5)")]
        [TestCase("transform", "scaleX(7) skewX(90deg) rotateX(1deg) translateX(1px)", "scaleX(7) skewX(90deg) rotateX(1deg) translateX(1px)")]
        [TestCase("clip-path", "circle(closest-side)", "circle()")]
        [TestCase("clip-path", "circle(4% at top right)", "circle(4% at right top)")]
        [TestCase("clip-path", "ellipse(closest-side closest-side at 10% 20%)", "ellipse(at 10% 20%)")]
        [TestCase("clip-path", "ellipse(1px closest-side)", "ellipse(1px closest-side)")]
        [TestCase("clip-path", "polygon(nonzero round 1px, 1px 2px, 3em 4em)", "polygon(round 1px, 1px 2px, 3em 4em)")]
        [TestCase("clip-path", "polygon(round 0px, 1px 2px, 3px 4px)", "polygon(1px 2px, 3px 4px)")]
        [TestCase("clip-path", "path(nonzero, \"M20,20h60 v60 h-60z\")", "path(\"M 20 20 h 60 v 60 h -60 Z\")")]
        [TestCase("clip-path", "xywh(0px 1% 2px 3em round 0)", "xywh(0px 1% 2px 3em)")]
        [TestCase("clip-path", "rect(0 100% 200px 4em)", "rect(0px 100% 200px 4em)")]
        [TestCase("clip-path", "shape(nonzero from 0px 0px, line to 10px 10px)", "shape(from 0px 0px, line to 10px 10px)")]
        [TestCase("clip-path", "shape(from 10px 10px, arc to 50px 1pt of 10px small rotate 0deg)", "shape(from 10px 10px, arc to 50px 1pt of 10px)")]
        [TestCase("animation-range-start", "cover 0%", "cover")]
        [TestCase("animation-range-end", "exit 1%, cover 2%, contain 100%", "exit 1%, cover 2%, contain")]
        // Chrome 155: finite float bound; numeric syntax tokens remain unbounded.
        [TestCase("opacity", "1e999", "3.40282e+38")]
        [TestCase("width", "1e999px", "3.40282e+38px")]
        [TestCase("z-index", "10000000", "10000000")]
        // Chrome 155: all CSS integer literals use the signed 32-bit supported range.
        [TestCase("z-index", "2147483648", "2147483647")]
        [TestCase("z-index", "-2147483649", "-2147483648")]
        [TestCase("z-index", "999999999999999999999999999999999999999999999999", "2147483647")]
        [TestCase("counter-increment", "chapter 10000000", "chapter 10000000")]
        [TestCase("animation-timing-function", "steps(10000000)", "steps(10000000)")]
        [TestCase("clip", "rect(1px, 2px, 3px, 4px)", "rect(1px, 2px, 3px, 4px)")]
        [TestCase("animation-timing-function", "linear(calc(0/0), 1)", "linear(0 0%, 1 100%)")]
        [TestCase("animation-name", "\"something\"", "something")]
        [TestCase("animation-name", "\"multi word string\"", "multi\\ word\\ string")]
        [TestCase("animation-name", "\"none\"", "\"none\"")]
        [TestCase("position-try-fallbacks", "flip-start flip-inline, flip-block", "flip-start flip-inline, flip-block")]
        [TestCase("position-try-fallbacks", "--bar flip-inline flip-block", "--bar flip-inline flip-block")]
        [TestCase("clip-path", "inset(1px 2px 3px 4px round 0px / 0%)", "inset(1px 2px 3px 4px round 0px / 0%)")]
        public void CanonicalSpecifiedLonghand(string property, string input, string expected)
        {
            ParsedProperty? parsed = Parse(property, input);
            Assert.That(parsed, Is.Not.Null, input);
            Assert.That(parsed!.Longhands.Single().Value.Serialize(), Is.EqualTo(expected));
        }

        [TestCase("align-content", "baseline last")]
        [TestCase("align-items", "flow-end")]
        [TestCase("justify-items", "flow-end")]
        [TestCase("font-language-override", "\"turkish\"")]
        [TestCase("font-language-override", "\"xøx\"")]
        [TestCase("font-language-override", "\"\"")]
        [TestCase("font-synthesis", "style oblique-only")]
        [TestCase("list-style-type", "symbols(numeric \"a\")")]
        [TestCase("list-style-type", "symbols(alphabetic \"a\")")]
        [TestCase("list-style-type", "symbols(fixed url(\"https://example.com/\"))")]
        [TestCase("clip-path", "circle(10% 20%)")]
        [TestCase("clip-path", "ellipse(closest-side)")]
        [TestCase("clip-path", "polygon(round 1px 1px 2px)")]
        [TestCase("top", "anchor(bottom 1px)")]
        [TestCase("z-index", "calc(10% / 20%)")]
        [TestCase("z-index", "1e999")]
        [TestCase("padding", "1pK")]
        [TestCase("animation-name", "\"\"")]
        [TestCase("padding-blocK-start", "1px")]
        [TestCase("width", "sin(1Khz)")]
        [TestCase("clip-path", "path(\"M 0 0 ſ 1 2 3 4\")")]
        [TestCase("grid-template-columns", "[a] [b] 10px")]
        public void InvalidOrUnshippedValueIsNotAccepted(string property, string input)
            => Assert.That(Parse(property, input), Is.Null);

        [Test]
        public void LiteralSupportBoundDoesNotChangeSyntaxTokenOrMathInfinity()
        {
            var tokens = CssParser.ParseComponentValueList("1e999");
            Assert.That(tokens.Single(), Is.TypeOf<PreservedToken>());
            Assert.That(double.IsPositiveInfinity(((PreservedToken)tokens.Single()).Token.Number), Is.True);
            Assert.That(Parse("opacity", "calc(infinity)")!.Longhands.Single().Value, Is.TypeOf<NecronomiDom.Css.Values.Math.CssMathValue>());
            Assert.That(Parse("opacity", "calc(infinity)")!.Longhands.Single().Value.Serialize(), Is.EqualTo("calc(infinity)"));
        }

        [TestCase("white-space", "collapse nowrap", "nowrap")]
        [TestCase("list-style", "disc outside none", "outside")]
        [TestCase("list-style", "none inside", "inside none")]
        [TestCase("list-style", "outside outside", "outside outside")]
        [TestCase("list-style", "square url(\"https://example.com/\") inside", "inside url(\"https://example.com/\") square")]
        [TestCase("border-image", "none", "none 100% / 1 / 0 stretch")]
        [TestCase("border-image", "1 / / 1px", "none 1 / 1 / 1px stretch")]
        [TestCase("text-box", "none auto", "normal")]
        [TestCase("text-box", "auto", "trim-both")]
        [TestCase("text-box", "trim-both cap text", "cap")]
        [TestCase("font-synthesis", "oblique-only weight", "weight oblique-only")]
        [TestCase("grid-template", "\"a\" [a] [b] \"b\"", "\"a\" [a b] \"b\"")]
        [TestCase("animation-range", "entry 0% exit 100%", "entry exit")]
        [TestCase("animation-range", "cover 50%", "cover 50%")]
        [TestCase("animation-range", "0% 100%", "0%")]
        [TestCase("animation-range", "100px normal", "100px")]
        [TestCase("position-try", "most-height none", "most-height none")]
        [TestCase("position-try", "normal none", "none")]
        [TestCase("position-try", "most-block-size flip-inline flip-block, --bar", "most-block-size flip-inline flip-block, --bar")]
        public void CanonicalSpecifiedShorthand(string property, string input, string expected)
        {
            ParsedProperty? parsed = Parse(property, input);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(ShorthandSerializer.TrySerialize(property, name => parsed!.Longhands.FirstOrDefault(p => p.Longhand == name).Value, out string value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        [Test]
        public void BackgroundInitialColorHasConstructedProvenanceAndDoesNotResetBlending()
        {
            ParsedProperty background = Parse("background", "none")!;
            Assert.That(background.Longhands.Single(p => p.Longhand == "background-color").Value.Serialize(), Is.EqualTo("rgba(0, 0, 0, 0)"));
            Assert.That(background.Longhands.Any(p => p.Longhand == "background-blend-mode"), Is.False);
        }

        private static ParsedProperty? Parse(string property, string input)
            => CssPropertyParser.Parse(property, CssParser.ParseComponentValueList(input));
    }
}
