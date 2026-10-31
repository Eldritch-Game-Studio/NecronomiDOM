using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathParserTests
    {
        [TestCase("calc(1px + 2em)", "length")]
        [TestCase("calc(1px + 2%)", "length-percentage")]
        [TestCase("calc(1deg + 2%)", "angle-percentage")]
        [TestCase("calc(1s + 20ms)", "time")]
        [TestCase("calc(1khz + 20hz)", "frequency")]
        [TestCase("calc(1dpi + 2dppx)", "resolution")]
        [TestCase("calc(1fr * 2)", "flex")]
        [TestCase("calc(1px / 2px)", "number")]
        [TestCase("calc(2px * 3px / 1px)", "length")]
        [TestCase("calc((1px / 1s) * 2s)", "length")]
        [TestCase("calc(100% / 3)", "percentage")]
        [TestCase("atan2(1px, 2em)", "angle")]
        [TestCase("hypot(3px, 4em)", "length")]
        [TestCase("calc(1.5)", "integer")]
        public void RecognizesDimensionalAlgebra(string input, string grammar)
        {
            CssMathValue value = Parse(input);
            Assert.That(value.Fits(grammar), Is.True);
        }

        [TestCase("calc()")]
        [TestCase("calc(1px + 1s)")]
        [TestCase("calc(0 + 1px)")]
        [TestCase("calc(1 + 1%)")]
        [TestCase("calc(1px * 1px)")]
        [TestCase("calc(1px / 1s)")]
        [TestCase("calc(5px - 5px + 10s)")]
        [TestCase("calc(0 * 5px + 10s)")]
        [TestCase("calc(1px+ 2px)")]
        [TestCase("calc(1px +2px)")]
        [TestCase("calc(1px/**/+/**/2px)")]
        [TestCase("calc(-(1px))")]
        [TestCase("calc(1madeup)")]
        [TestCase("calc(mystery(2))")]
        [TestCase("calc(nonsense)")]
        [TestCase("calc(1, 2)")]
        [TestCase("min()")]
        [TestCase("min(1px, 1s)")]
        [TestCase("min(1px,)")]
        [TestCase("clamp(1px, 2px)")]
        [TestCase("clamp(1px, none, 2px)")]
        [TestCase("round(sideways, 2, 1)")]
        [TestCase("round(1px)")]
        [TestCase("round(1px, 1s)")]
        [TestCase("mod(1px, 2)")]
        [TestCase("rem(1)")]
        [TestCase("sin(1px)")]
        [TestCase("cos(1%)")]
        [TestCase("tan(1, 2)")]
        [TestCase("asin(1deg)")]
        [TestCase("acos(1%)")]
        [TestCase("atan2(1deg, 1px)")]
        [TestCase("pow(1px, 2)")]
        [TestCase("sqrt(1px)")]
        [TestCase("hypot(1px, 1s)")]
        [TestCase("log(1px)")]
        [TestCase("exp(1s)")]
        [TestCase("abs(1, 2)")]
        [TestCase("sign()")]
        public void RejectsInvalidGrammarOrDimension(string input)
        {
            var function = CssParser.ParseComponentValue(input) as FunctionValue;
            Assert.That(function, Is.Not.Null);
            Assert.That(CssMathParser.TryParse(function!, out CssValue? value), Is.False);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void PercentHintDoesNotTurnLengthPercentageIntoLength()
        {
            CssMathValue mixed = Parse("calc(1px + 1%)");
            Assert.That(mixed.Type.PercentageHint, Is.EqualTo(CssNumericDimension.Length));
            Assert.That(mixed.Fits("length"), Is.False);
            Assert.That(mixed.Fits("length-percentage"), Is.True);
        }

        [Test]
        public void NumberPercentageCoercionRequiresAnExplicitGrammarContext()
        {
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(25% + 0.25)")!;
            Assert.That(CssMathParser.TryParse(function, out _), Is.False);
            Assert.That(CssMathParser.TryParse(function, CssNumericDimension.Number, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            Assert.That(math.Type.PercentageHint, Is.EqualTo(CssNumericDimension.Number));
            Assert.That(math.Fits("number-percentage"), Is.True);
            Assert.That(math.Fits("number"), Is.False);
            Assert.That(math.TryGetConstant(out double result), Is.True);
            Assert.That(result, Is.EqualTo(0.5));
            Assert.That(math.Serialize(), Is.EqualTo("calc(0.5)"));
        }

        [Test]
        public void ChannelSymbolsAreExplicitAndDimensionless()
        {
            var symbols = new Dictionary<string, double> { ["r"] = 120, ["g"] = 20 };
            var function = (FunctionValue)CssParser.ParseComponentValue("calc(r + max(g, 10) * 2)")!;
            Assert.That(CssMathParser.TryParse(function, symbols, out CssValue? parsed), Is.True);
            var math = (CssMathValue)parsed!;
            Assert.That(math.TryGetConstant(out double result), Is.True);
            Assert.That(result, Is.EqualTo(160));
            function = (FunctionValue)CssParser.ParseComponentValue("calc(unknown + r)")!;
            Assert.That(CssMathParser.TryParse(function, symbols, out _), Is.False);
        }

        internal static CssMathValue Parse(string input)
        {
            var function = CssParser.ParseComponentValue(input) as FunctionValue;
            Assert.That(function, Is.Not.Null, input);
            Assert.That(CssMathParser.TryParse(function!, out CssValue? value), Is.True, input);
            Assert.That(value, Is.TypeOf<CssMathValue>());
            return (CssMathValue)value!;
        }
    }
}
