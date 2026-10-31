using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Images
{
    [TestFixture]
    public sealed class CssDynamicImageTests
    {
        // Chrome 155 specified-value serialization.
        [TestCase("light-dark(url(a.png), none)","light-dark(url(\"a.png\"), none)")]
        [TestCase("image(transparent)","image(transparent)")]
        [TestCase("light-dark(linear-gradient(red, blue), url(a.png))","light-dark(linear-gradient(red, blue), url(\"a.png\"))")]
        public void MatchesChromeDynamicImageSerialization(string text,string expected)
        {
            CssValue value=Parse(text);
            Assert.That(value.Serialize(),Is.EqualTo(expected));
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(value.Serialize())!,out _),Is.True);
        }
        [TestCase("image(red, blue)")]
        [TestCase("image(url(a.png))")]
        [TestCase("image(unknown-color)")]
        [TestCase("light-dark(red, url(a.png))")]
        [TestCase("light-dark(url(a.png), none, none)")]
        public void RejectsInvalidDynamicImageDomains(string text)
        { Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(text)!,out _),Is.False); }
        [Test]
        public void SchemeSelectionKeepsImageAndMakesNoneARealTransparentImage()
        {
            var value=(CssLightDarkImageValue)Parse("light-dark(url(a.png), none)");
            Assert.That(value.Select(false).Serialize(),Is.EqualTo("url(\"a.png\")"));
            Assert.That(value.Select(true),Is.TypeOf<CssSolidColorImageValue>());
            Assert.That(value.Select(true).Serialize(),Is.EqualTo("image(transparent)"));
        }
        private static CssValue Parse(string text)
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(text)!,out CssValue? value),Is.True);
            return value!;
        }
    }
}
