using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Images
{
    [TestFixture]
    public sealed class CssImageParserTests
    {
        [TestCase("url(test.png)")]
        [TestCase("url(\"test.png\")")]
        [TestCase("linear-gradient(red, blue)")]
        [TestCase("linear-gradient(to top left, red 10% 20%, blue)")]
        [TestCase("linear-gradient(45deg in oklch longer hue, red, 25%, blue)")]
        [TestCase("repeating-linear-gradient(in srgb 1turn, red 0px, blue 10px)")]
        [TestCase("radial-gradient(circle closest-side at left top, red, blue)")]
        [TestCase("radial-gradient(ellipse 10px 20% at 25% 75% in lab, red, blue)")]
        [TestCase("radial-gradient(15px at center, red, blue)")]
        [TestCase("radial-gradient(circle calc(-1px), red, blue)")]
        [TestCase("radial-gradient(ellipse calc(-1px) calc(-2%), red, blue)")]
        [TestCase("repeating-radial-gradient(red, blue)")]
        [TestCase("conic-gradient(from 45deg at center in oklab, red 0deg 20deg, blue 100%)")]
        [TestCase("repeating-conic-gradient(red, 25%, blue 1turn)")]
        [TestCase("image-set(\"a.png\" 1x, url(b.png) 192dpi type(\"image/png\"))")]
        [TestCase("image-set(linear-gradient(red, blue) type(\"image/png\") 2dppx)")]
        [TestCase("image-set(url(a.png) 0x)")]
        [TestCase("image-set(url(a.png) calc(-1x))")]
        public void AcceptsMandatoryImageGrammar(string input)
        {
            ComponentValue? component = CssParser.ParseComponentValue(input);
            Assert.That(component,Is.Not.Null);
            Assert.That(CssImageParser.TryParse(component!,out CssValue? value),Is.True);
            Assert.That(value,Is.Not.Null);
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(value!.Serialize())!,out _),Is.True);
        }
        [TestCase("unknown-image(red)")]
        [TestCase("linear-gradient(red)")]
        [TestCase("linear-gradient(to left right, red, blue)")]
        [TestCase("linear-gradient(50%, red, blue)")]
        [TestCase("linear-gradient(red, nonsense, blue)")]
        [TestCase("linear-gradient(red, 20%, 30%, blue)")]
        [TestCase("linear-gradient(20%, red, blue)")]
        [TestCase("linear-gradient(red, blue, 20%)")]
        [TestCase("linear-gradient(red 10deg, blue)")]
        [TestCase("linear-gradient(in unknown, red, blue)")]
        [TestCase("linear-gradient(in srgb shorter hue, red, blue)")]
        [TestCase("radial-gradient(circle 10%, red, blue)")]
        [TestCase("radial-gradient(ellipse 10px, red, blue)")]
        [TestCase("radial-gradient(10px 20px 30px, red, blue)")]
        // Chrome 155: generic gradient position permits one/two/four values, not three.
        [TestCase("radial-gradient(at bottom 10% right, red, blue)")]
        [TestCase("radial-gradient(at bottom right 20%, red, blue)")]
        [TestCase("radial-gradient(circle -1px, red, blue)")]
        [TestCase("radial-gradient(at left right, red, blue)")]
        [TestCase("conic-gradient(from 20%, red, blue)")]
        [TestCase("conic-gradient(red 1px, blue)")]
        [TestCase("image-set()")]
        [TestCase("image-set(url(a.png) -1x)")]
        [TestCase("image-set(url(a.png) 1x 2x)")]
        [TestCase("image-set(url(a.png) type(image/png))")]
        [TestCase("image-set(url(a.png) mystery())")]
        [TestCase("image-set(url(a.png),)")]
        public void RejectsInvalidImageGrammar(string input) => Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(input)!,out _),Is.False);

        [TestCase("linear-gradient(red 10% 20%, 40%, blue)","srgb")]
        [TestCase("linear-gradient(color(srgb 1 0 0) 10% 20%, 40%, color(srgb 0 0 1))","oklab")]
        public void GradientsChooseDefaultInterpolationFromTypedColorSyntax(string input,string expectedSpace)
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(input)!,out CssValue? value),Is.True);
            var gradient = (CssGradientValue)value!;
            Assert.That(gradient.InterpolationSpace,Is.EqualTo(expectedSpace));
            Assert.That(gradient.Stops[0].Positions.Count,Is.EqualTo(2));
            Assert.That(gradient.Stops[1].Color,Is.Null);
            Assert.That(gradient.Stops[2].Color,Is.Not.Null);
        }
        // Chrome 155 serialization: named stops, retained endpoints, omitted numeric circle.
        [TestCase("linear-gradient(to bottom, red 0%, blue 100%)", "linear-gradient(red 0%, blue 100%)")]
        [TestCase("linear-gradient(to top left, red, blue)", "linear-gradient(to left top, red, blue)")]
        [TestCase("radial-gradient(farthest-corner ellipse at center, red, blue)", "radial-gradient(red, blue)")]
        [TestCase("radial-gradient(15px at center, red, blue)", "radial-gradient(15px, red, blue)")]
        [TestCase("conic-gradient(from 0deg at 50% 50%, red, blue)", "conic-gradient(red, blue)")]
        [TestCase("linear-gradient(in oklab, red, blue)","linear-gradient(in oklab, red, blue)")]
        [TestCase("linear-gradient(in srgb, red, blue)","linear-gradient(red, blue)")]
        [TestCase("radial-gradient(circle calc(-1px), red, blue)","radial-gradient(calc(-1px), red, blue)")]
        [TestCase("image-set('a.png')", "image-set(url(\"a.png\") 1x)")]
        public void SerializesCanonicalImageValues(string input,string expected)
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(input)!,out CssValue? value),Is.True);
            Assert.That(value!.Serialize(),Is.EqualTo(expected));
        }
        [TestCase("at bottom right","at right bottom")]
        [TestCase("at bottom 10% right 20%","at right 20% bottom 10%")]
        public void CanonicalizesHorizontalPositionGroupBeforeVertical(string input,string expected)
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue("radial-gradient("+input+", red, blue)")!,out CssValue? value),Is.True);
            Assert.That(((CssGradientValue)value!).Prelude!.Serialize(),Is.EqualTo(expected));
        }
        [Test]
        public void UrlSerializationUsesCssomStringEscaping()
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue("url(a.png)")!,out CssValue? value),Is.True);
            Assert.That(value!.Serialize(),Is.EqualTo("url(\"a.png\")"));
        }
    }
}
