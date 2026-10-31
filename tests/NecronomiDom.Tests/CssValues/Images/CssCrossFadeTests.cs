using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Images
{
    [TestFixture]
    public sealed class CssCrossFadeTests
    {
        // Chrome 155 specified-value serialization.
        [Test]
        public void ShippedLegacySyntaxHasValidatedImagesAndNormalizedProgress()
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue("-webkit-cross-fade(url(a.png), url(b.png), .5)")!,out CssValue? parsed),Is.True);
            var fade=(CssCrossFadeValue)parsed!;
            Assert.That(fade.Serialize(),Is.EqualTo("-webkit-cross-fade(url(\"a.png\"), url(\"b.png\"), 0.5)"));
            Assert.That(fade.From,Is.TypeOf<CssUrlValue>());
            Assert.That(fade.To,Is.TypeOf<CssUrlValue>());
            Assert.That(fade.TryResolveProgress(null,out double progress),Is.True);
            Assert.That(progress,Is.EqualTo(.5));
        }
        // Chrome 155: literal percentages normalize, math wrappers remain.
        [TestCase("-webkit-cross-fade(url(a), url(b), 50%)","-webkit-cross-fade(url(\"a\"), url(\"b\"), 0.5)")]
        [TestCase("-webkit-cross-fade(url(a), url(b), 2)","-webkit-cross-fade(url(\"a\"), url(\"b\"), 1)")]
        [TestCase("-webkit-cross-fade(none, url(b), .5)","-webkit-cross-fade(none, url(\"b\"), 0.5)")]
        [TestCase("-webkit-cross-fade(url(a), url(b), calc(50%))","-webkit-cross-fade(url(\"a\"), url(\"b\"), calc(50%))")]
        [TestCase("-webkit-cross-fade(url(a), url(b), calc(1em / 1px))","-webkit-cross-fade(url(\"a\"), url(\"b\"), calc(1em / 1px))")]
        public void MatchesMeasuredLegacyProgressSpecifiedSerialization(string text,string expected)
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(text)!,out CssValue? value),Is.True);
            Assert.That(value!.Serialize(),Is.EqualTo(expected));
        }
        [Test]
        public void ComputedMathProgressRetainsPercentageProvenanceAcrossCloning()
        {
            Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue("-webkit-cross-fade(url(a), url(b), calc(50%))")!,out CssValue? value),Is.True);
            var source=(CssCrossFadeValue)value!;
            Assert.That(source.ProgressFromMath,Is.True);
            var computed=new CssCrossFadeValue(source.From,source.To,new CssPercentageValue(50),source.ProgressFromMath);
            Assert.That(computed.ProgressFromMath,Is.True);
            Assert.That(computed.Progress.Serialize(),Is.EqualTo("50%"));
            Assert.That(computed.TryResolveProgress(null,out double ratio),Is.True);
            Assert.That(ratio,Is.EqualTo(.5));
        }
        [TestCase("-webkit-cross-fade(url(a.png), url(b.png))")]
        [TestCase("-webkit-cross-fade(url(a.png), url(b.png), .5, .25)")]
        [TestCase("-webkit-cross-fade(red, url(b.png), .5)")]
        [TestCase("-webkit-cross-fade(url(a.png), url(b.png), 5px)")]
        [TestCase("-webkit-cross-fade(url(a.png), url(b.png), unknown(.5))")]
        public void RejectsMalformedLegacyImageAndProgressDomains(string text)
        { Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue(text)!,out _),Is.False); }
        // Chrome 155 does not ship the unprefixed specification syntax.
        [Test]
        public void UnprefixedCrossFadeIsNotAnAliasForLegacySyntax()
        { Assert.That(CssImageParser.TryParse(CssParser.ParseComponentValue("cross-fade(url(a.png) 50%, url(b.png))")!,out _),Is.False); }
    }
}
