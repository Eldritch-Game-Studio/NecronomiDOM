using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    [TestFixture]
    public sealed class ComputedUnitTests
    {
        internal static readonly ComputedValueContext Context = new()
        {
            Font = new(20, 9, 11, 14, 20, 28, 20), ParentFont = new(16, 7, 8, 11, 16, 24, 16),
            RootFont = new(18, 8, 10, 12, 18, 26, 18), InitialFont = new(16, 8, 8, 11, 16, 24, 16),
            MediumFontSize = 16, ParentFontWeight = 400, DevicePixelRatio = 1,
            Viewport = new(1000, 600), LargeViewport = new(1000, 600),
            SmallViewport = new(800, 500), DynamicViewport = new(900, 550),
            ContainerUnitBasis = unit => unit is "cqw" or "cqi" ? 400 : unit is "cqh" or "cqb" ? 200 : null
        };

        [TestCase("in", 1, "96px")]
        [TestCase("cm", 2.54, "96px")]
        [TestCase("mm", 25.4, "96px")]
        [TestCase("Q", 101.6, "96px")]
        [TestCase("pt", 72, "96px")]
        [TestCase("pc", 6, "96px")]
        [TestCase("em", 2, "40px")]
        [TestCase("ex", 2, "18px")]
        [TestCase("ch", 2, "22px")]
        [TestCase("cap", 2, "28px")]
        [TestCase("ic", 2, "40px")]
        [TestCase("lh", 2, "56px")]
        [TestCase("rem", 2, "36px")]
        [TestCase("rex", 2, "16px")]
        [TestCase("rch", 2, "20px")]
        [TestCase("rcap", 2, "24px")]
        [TestCase("ric", 2, "36px")]
        [TestCase("rlh", 2, "52px")]
        [TestCase("vw", 10, "100px")]
        [TestCase("vh", 10, "60px")]
        [TestCase("vi", 10, "100px")]
        [TestCase("vb", 10, "60px")]
        [TestCase("vmin", 10, "60px")]
        [TestCase("vmax", 10, "100px")]
        [TestCase("svw", 10, "80px")]
        [TestCase("svh", 10, "50px")]
        [TestCase("lvh", 10, "60px")]
        [TestCase("dvh", 10, "55px")]
        [TestCase("cqw", 10, "40px")]
        [TestCase("cqh", 10, "20px")]
        [TestCase("cqi", 10, "40px")]
        [TestCase("cqb", 10, "20px")]
        [TestCase("cqmin", 10, "20px")]
        [TestCase("cqmax", 10, "40px")]
        public void LengthsUseActualProvidedMetrics(string unit, double magnitude, string expected)
        {
            Assert.That(ComputedValueResolver.Resolve("margin-left", new CssDimensionValue(magnitude, unit), Context).Serialize(), Is.EqualTo(expected));
        }

        [TestCase("ex")]
        [TestCase("ch")]
        [TestCase("cap")]
        [TestCase("ic")]
        [TestCase("lh")]
        [TestCase("rem")]
        [TestCase("dvh")]
        [TestCase("cqi")]
        public void MissingBasesDoNotInventPixels(string unit)
        {
            var specified = new CssDimensionValue(3, unit);
            Assert.That(ComputedValueResolver.Resolve("width", specified, default), Is.SameAs(specified));
        }

        [Test]
        public void ContainerFallbackRequiresKnownAbsence()
        {
            var context = Context with { ContainerUnitBasis = null };
            var specified = new CssDimensionValue(10, "cqi");
            Assert.That(ComputedValueResolver.Resolve("width", specified, context), Is.SameAs(specified));
            Assert.That(ComputedValueResolver.Resolve("width", specified, context with { NoEligibleContainer = true }).Serialize(), Is.EqualTo("80px"));
        }

        [Test]
        public void FontSizeAndLineHeightAvoidCircularMetricDependencies()
        {
            Assert.That(ComputedValueResolver.Resolve("font-size", new CssDimensionValue(2, "em"), Context).Serialize(), Is.EqualTo("32px"));
            Assert.That(ComputedValueResolver.Resolve("font-size", new CssDimensionValue(2, "rem"), Context with { IsRoot = true }).Serialize(), Is.EqualTo("32px"));
            Assert.That(ComputedValueResolver.Resolve("line-height", new CssDimensionValue(2, "lh"), Context).Serialize(), Is.EqualTo("48px"));
            Assert.That(ComputedValueResolver.Resolve("line-height", new CssNumberValue(1.5, false), Context).Serialize(), Is.EqualTo("1.5"));
            Assert.That(ComputedValueResolver.Resolve("line-height", new CssPercentageValue(150), Context).Serialize(), Is.EqualTo("30px"));
        }

        [TestCase("animation-delay", 250, "ms", "0.25s")]
        [TestCase("rotate", 0.25, "turn", "90deg")]
        [TestCase("rotate", 100, "grad", "90deg")]
        public void OtherDimensionsUseCanonicalUnits(string property, double magnitude, string unit, string expected)
        {
            Assert.That(ComputedValueResolver.Resolve(property, new CssDimensionValue(magnitude, unit), Context).Serialize(), Is.EqualTo(expected));
        }
    }
}
