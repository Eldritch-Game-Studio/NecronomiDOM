using NecronomiDom.Css.Values.Math;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Math
{
    [TestFixture]
    public sealed class CssMathSerializationTests
    {
        // Expected forms follow WPT css/css-values/*-serialize.html and calc-serialization-002.html.
        [TestCase("calc(1vh + 2px + 3%)", "calc(3% + 2px + 1vh)")]
        [TestCase("calc(-8px + 9em + 1vh)", "calc(9em - 8px + 1vh)")]
        [TestCase("calc(1pc + 1in + 1vh + 10%)", "calc(10% + 112px + 1vh)")]
        [TestCase("calc(100% - 100% + 1em)", "calc(0% + 1em)")]
        [TestCase("calc(4vmin + 0pt)", "calc(0px + 4vmin)")]
        [TestCase("calc(2 * (10px + 1rem))", "calc(20px + 2rem)")]
        [TestCase("calc((10px + 1rem) / 2)", "calc(5px + 0.5rem)")]
        [TestCase("min(1in)", "calc(96px)")]
        [TestCase("min(50px, 1in + 1px)", "calc(50px)")]
        [TestCase("max(50px, 1in + 1px)", "calc(97px)")]
        [TestCase("min(1px, 1em)", "min(1px, 1em)")]
        [TestCase("min(10%, 1em)", "min(10%, 1em)")]
        [TestCase("min(1% + 1px)", "calc(1% + 1px)")]
        [TestCase("min(1%, 2%, 3%)", "min(1%, 2%, 3%)")]
        [TestCase("min(-0%, 0%)", "min(0%, 0%)")]
        [TestCase("calc(1px + min(1in, 100px))", "calc(97px)")]
        [TestCase("calc(2 * (.2 * min(1em, 1px)) + 1px)", "calc(1px + (0.4 * min(1em, 1px)))")]
        [TestCase("calc(2 * (min(10px, 20%) + max(1rem, 2%)))", "calc(2 * (min(10px, 20%) + max(1rem, 2%)))")]
        [TestCase("calc(min(1%, 2%) + max(3%, 4%) + 10%)", "calc(10% + min(1%, 2%) + max(3%, 4%))")]
        [TestCase("calc(pow(2, sign(1em - 18px)))", "calc(pow(2, sign(1em - 18px)))")]
        [TestCase("round(1.1, 1)", "calc(1)")]
        [TestCase("mod(1, 1)", "calc(0)")]
        [TestCase("abs(-3)", "calc(3)")]
        [TestCase("calc(1px * NaN)", "calc(NaN * 1px)")]
        [TestCase("calc(infinity * 1in)", "calc(infinity * 1px)")]
        [TestCase("calc(-infinity * 1%)", "calc(-infinity * 1%)")]
        [TestCase("calc(1 / 0)", "calc(infinity)")]
        [TestCase("calc(0 / 0)", "calc(NaN)")]
        [TestCase("calc(-0)", "calc(0)")]
        [TestCase("clamp(none, 1em, 10px)", "clamp(none, 1em, 10px)")]
        public void SerializesCanonicalSpecifiedMath(string input, string expected)
        {
            Assert.That(CssMathParserTests.Parse(input).Serialize(), Is.EqualTo(expected));
        }

        [TestCase("px")][TestCase("in")][TestCase("cm")][TestCase("mm")][TestCase("q")][TestCase("pt")][TestCase("pc")]
        [TestCase("em")][TestCase("ex")][TestCase("cap")][TestCase("ch")][TestCase("ic")][TestCase("lh")]
        [TestCase("rem")][TestCase("rex")][TestCase("rcap")][TestCase("rch")][TestCase("ric")][TestCase("rlh")]
        [TestCase("vw")][TestCase("vh")][TestCase("vi")][TestCase("vb")][TestCase("vmin")][TestCase("vmax")]
        [TestCase("svw")][TestCase("svh")][TestCase("svi")][TestCase("svb")][TestCase("svmin")][TestCase("svmax")]
        [TestCase("lvw")][TestCase("lvh")][TestCase("lvi")][TestCase("lvb")][TestCase("lvmin")][TestCase("lvmax")]
        [TestCase("dvw")][TestCase("dvh")][TestCase("dvi")][TestCase("dvb")][TestCase("dvmin")][TestCase("dvmax")]
        [TestCase("cqw")][TestCase("cqh")][TestCase("cqi")][TestCase("cqb")][TestCase("cqmin")][TestCase("cqmax")]
        public void SupportsEveryLengthUnit(string unit)
        {
            CssMathValue math = CssMathParserTests.Parse("calc(2" + unit + ")");
            Assert.That(math.Fits("length"), Is.True);
            Assert.That(math.TryEvaluate(new CssMathEvaluationContext(_ => 4), out _), Is.True);
        }

        [Test]
        public void ComputedSimplificationRetainsUnresolvedPercentages()
        {
            CssMathValue math = CssMathParserTests.Parse("min(1em, 10%)");
            var context = new CssMathEvaluationContext(unit => unit == "em" ? 16 : null);
            Assert.That(math.Simplify(context).Serialize(), Is.EqualTo("min(16px, 10%)"));
        }
    }
}
