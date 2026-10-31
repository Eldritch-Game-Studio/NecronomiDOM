using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    [TestFixture]
    public sealed class ComputedColorImageTests
    {
        [TestCase("red", "rgb(255, 0, 0)")]
        [TestCase("#00ff00", "rgb(0, 255, 0)")]
        [TestCase("transparent", "rgba(0, 0, 0, 0)")]
        [TestCase("rgb(0 0 255 / 0.5)", "rgba(0, 0, 255, 0.5)")]
        [TestCase("color(srgb 1 0 0)", "color(srgb 1 0 0)")]
        [TestCase("oklab(0.5 0.1 0.2)", "oklab(0.5 0.1 0.2)")]
        [TestCase("color-mix(in srgb, red, blue)", "color(srgb 0.5 0 0.5)")]
        [TestCase("rgb(from red r g b / 0.5)", "color(srgb 1 0 0 / 0.5)")]
        public void ColorFourAndFiveValuesAreCanonical(string specified, string expected)
        {
            CssValue value = ComputedPropertyTests.Parse("background-color", specified);
            Assert.That(ComputedValueResolver.Resolve("background-color", value, ComputedUnitTests.Context).Serialize(), Is.EqualTo(expected));
        }

        // CSS Color 4 absolute colors are context-independent; the assigned steady-state allocation goal is zero here.
        [TestCase("red")]
        [TestCase("rgb(10 20 30 / 0.5)")]
        [TestCase("color(display-p3 0.2 0.3 0.4)")]
        [TestCase("oklab(none 0.1 0.2)")]
        public void ImmutableAbsoluteColorResolutionSharesItsResultWithoutWarmAllocation(string text)
        {
            CssValue value = ComputedPropertyTests.Parse("background-color", text);
            var context = ComputedUnitTests.Context;
            CssValue first = ComputedValueResolver.Resolve("background-color", value, context);
            Assert.That(ComputedValueResolver.Resolve("background-color", value, context with { DarkColorScheme = true }), Is.SameAs(first));
            for (int i = 0; i < 100; i++) _ = ComputedValueResolver.Resolve("background-color", value, context);
            CssValue last = first;
            long allocated = AllocationWindow.Measure(1000, _ => last = ComputedValueResolver.Resolve("background-color", value, context));
            Assert.That(last, Is.SameAs(first));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void CurrentColorOnColorInheritsButOtherPropertiesUseElementColor()
        {
            var context = ComputedUnitTests.Context with
            {
                CurrentColor = new ComputedValueColor(new("srgb", 1, 0, 0), true),
                InheritedColor = new ComputedValueColor(new("srgb", 0, 0, 1), true)
            };
            CssValue current = ComputedPropertyTests.Parse("color", "currentcolor");
            Assert.That(ComputedValueResolver.Resolve("color", current, context).Serialize(), Is.EqualTo("rgb(0, 0, 255)"));
            Assert.That(ComputedValueResolver.Resolve("background-color", current, context).Serialize(), Is.EqualTo("rgb(255, 0, 0)"));
            Assert.That(ComputedValueResolver.Resolve("color", current, default), Is.SameAs(current));
        }

        [Test]
        public void LightDarkSelectsOnlyApplicableBranch()
        {
            CssValue choice = ComputedPropertyTests.Parse("color", "light-dark(red, blue)");
            Assert.That(ComputedValueResolver.Resolve("color", choice, ComputedUnitTests.Context).Serialize(), Is.EqualTo("rgb(255, 0, 0)"));
            Assert.That(ComputedValueResolver.Resolve("color", choice, ComputedUnitTests.Context with { DarkColorScheme = true }).Serialize(), Is.EqualTo("rgb(0, 0, 255)"));
            choice = ComputedPropertyTests.Parse("color", "light-dark(red, currentcolor)");
            Assert.That(ComputedValueResolver.Resolve("color", choice, default).Serialize(), Is.EqualTo("rgb(255, 0, 0)"));
        }

        [Test]
        public void SystemPaletteIsNeverInvented()
        {
            CssValue system = ComputedPropertyTests.Parse("color", "CanvasText");
            Assert.That(ComputedValueResolver.Resolve("color", system, default), Is.SameAs(system));
            var context = ComputedUnitTests.Context with { SystemColor = name => name == "canvastext" ? new CssResolvedColor("srgb", 0, 0, 0) : null };
            Assert.That(ComputedValueResolver.Resolve("color", system, context).Serialize(), Is.EqualTo("rgb(0, 0, 0)"));
        }

        [Test]
        public void SystemColorResolutionRemainsLiveAfterResolvingTheSameKeyword()
        {
            CssValue system = ComputedPropertyTests.Parse("color", "CanvasText");
            int calls = 0;
            var context = ComputedUnitTests.Context with { SystemColor = _ => { calls++; return new CssResolvedColor("srgb", 1, 0, 0); } };
            Assert.That(ComputedValueResolver.Resolve("color", system, context).Serialize(), Is.EqualTo("rgb(255, 0, 0)"));
            context = context with { SystemColor = _ => { calls++; return new CssResolvedColor("srgb", 0, 0, 1); } };
            Assert.That(ComputedValueResolver.Resolve("color", system, context).Serialize(), Is.EqualTo("rgb(0, 0, 255)"));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(ComputedValueResolver.Resolve("color", system, default), Is.SameAs(system));
        }

        [Test]
        public void GradientStopsAndImageSetResolutionsNormalizeRecursively()
        {
            CssValue image = ComputedPropertyTests.Parse("background-image", "linear-gradient(red 1em, blue 2rem)");
            Assert.That(ComputedValueResolver.Resolve("background-image", image, ComputedUnitTests.Context).Serialize(), Is.EqualTo("linear-gradient(rgb(255, 0, 0) 20px, rgb(0, 0, 255) 36px)"));
            var imageSet = new CssImageSetValue(new[] { new CssImageSetOption(new CssUrlValue("image.png"), new CssDimensionValue(192, "dpi"), null) });
            var context = ComputedUnitTests.Context with { DocumentUrl = WebUrl.Parse("res:///ui/page.html") };
            Assert.That(ComputedValueResolver.Resolve("background-image", imageSet, context).Serialize(), Is.EqualTo("image-set(url(\"res:///ui/image.png\") 2dppx)"));
        }
    }
}
