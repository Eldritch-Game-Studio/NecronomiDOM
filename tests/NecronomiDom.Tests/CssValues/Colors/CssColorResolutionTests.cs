using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Colors
{
    [TestFixture]
    public sealed class CssColorResolutionTests
    {
        // Upstream WPT css/css-color/parsing/color-computed-relative-color.html.
        [TestCase("lch(from red calc((49.44 - l) * infinity) 0 0)","lch(0 0 0)")]
        [TestCase("lch(from blue calc((49.44 - l) * infinity) 0 0)","lch(100 0 0)")]
        public void RelativeLabLightnessRespectsItsDefinedBounds(string text,string expected)
        {
            Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out CssValue? value),Is.True);
            Assert.That(((CssColorValue)value!).TryResolve(new CssColorContext(),out CssResolvedColor color),Is.True);
            Assert.That(color.SerializeComputed(),Is.EqualTo(expected));
        }
        // Upstream WPT color-valid-relative-color.html and alpha-color-parsing-valid.html.
        [TestCase("rgb(from rebeccapurple r g 20% / alpha)","rgb(from rebeccapurple r g 20% / alpha)")]
        [TestCase("hsl(from rebeccapurple 25deg s l / alpha)","hsl(from rebeccapurple 25deg s l / alpha)")]
        [TestCase("lch(from lch(0.7 45 30 / 40%) 50 120 -400deg / -500)","lch(from lch(0.7 45 30 / 0.4) 50 120 -400deg / -500)")]
        [TestCase("alpha(from red / 2)","alpha(from red / 2)")]
        [TestCase("alpha(from red / -1)","alpha(from red / -1)")]
        public void RelativeAndModifierChannelsRetainTheirSpecifiedRepresentation(string text,string expected)
        {
            Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out CssValue? value),Is.True);
            Assert.That(value!.Serialize(),Is.EqualTo(expected));
        }
        // Chrome 155 inline-style serialization.
        [TestCase("#11223344","rgba(17, 34, 51, 0.267)")]
        [TestCase("#abcdef80","rgba(171, 205, 239, 0.5)")]
        [TestCase("rgba(1, 2, 3, .123456)","rgba(1, 2, 3, 0.12)")]
        [TestCase("rgba(1, 2, 3, .9999)","rgb(1, 2, 3)")]
        public void LegacyCssomQuantizesAlphaBeforeChoosingOpaqueFunction(string text,string expected)
        {
            Assert.That(CssColorParser.TryParse(CssParser.ParseComponentValueList(text),out CssValue? value),Is.True);
            Assert.That(value!.Serialize(),Is.EqualTo(expected));
        }
    }
}
