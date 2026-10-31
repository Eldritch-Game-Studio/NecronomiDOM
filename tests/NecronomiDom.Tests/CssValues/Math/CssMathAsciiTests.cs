using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathAsciiTests
    {
        [TestCase("CALC(2PX + 3px)", "calc(5px)")]
        [TestCase("SIN(90DEG)", "calc(1)")]
        [TestCase("ROUND(NEAREST, 1.4, 1)", "calc(1)")]
        public void CssNumericNamesAreAsciiCaseInsensitive(string input, string expected)
        {
            Assert.That(CssMathParserTests.Parse(input).Serialize(), Is.EqualTo(expected));
        }

        [TestCase("calc(1\u212Ahz)")]
        [TestCase("\u017Fin(0)")]
        [TestCase("calc(\u0130NFINITY)")]
        [TestCase("round(neare\u017Ft, 1.4, 1)")]
        public void UnicodeConfusablesDoNotBecomeCssNumericKeywords(string input)
        {
            var function = (FunctionValue)CssParser.ParseComponentValue(input)!;
            Assert.That(CssMathParser.TryParse(function, out _), Is.False);
        }

        [Test]
        public void RegisteredChannelsUseAsciiRatherThanUnicodeCaseFolding()
        {
            var fake = (FunctionValue)CssParser.ParseComponentValue("calc(\u017Fize + 1)")!;
            Assert.That(CssMathParser.TryParseChannels(fake, new[] { "size" }, out _), Is.False);
            var actual = (FunctionValue)CssParser.ParseComponentValue("calc(R + 1)")!;
            Assert.That(CssMathParser.TryParseChannels(actual, new[] { "r" }, out CssValue? value), Is.True);
            Assert.That(value!.Serialize(), Is.EqualTo("calc(1 + r)"));
        }

        [Test]
        public void CallerUnicodeDictionaryComparerCannotIntroduceUnknownChannel()
        {
            var symbols = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["s"] = 1 };
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(\u017F + 1)")!;
            Assert.That(CssMathParser.TryParse(function, symbols, out _), Is.False);
        }

        [Test]
        public void SizingSymbolsAlsoUseAsciiMatching()
        {
            var fake = (FunctionValue)CssParser.ParseComponentValue("calc-size(auto, \u017Fize + 1px)")!;
            Assert.That(CssMathParser.TryParseSizing(fake, out _), Is.False);
            var actual = (FunctionValue)CssParser.ParseComponentValue("CALC-SIZE(AUTO, SIZE + 1PX)")!;
            Assert.That(CssMathParser.TryParseSizing(actual, out CssValue? value), Is.True);
            Assert.That(value!.Serialize(), Is.EqualTo("calc-size(auto, 1px + size)"));
        }
    }
}
