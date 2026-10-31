using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Values;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade.ComputedValues
{
    public sealed class ComputedLogicalPositionTests
    {
        // CSS Values 4 positions
        // already expressed as physical coordinates need no new list or copied coordinate buffer.
        [TestCase("object-position")]
        [TestCase("perspective-origin")]
        [TestCase("transform-origin")]
        [TestCase("mask-position")]
        [NonParallelizable]
        public void CanonicalPhysicalPositionDoesNotAllocateDuringRepeatedResolution(string property)
        {
            var specified = ComputedPropertyTests.Parse(property, "25% 75%");
            var context = new ComputedValueContext { PropertyValue = static name => name == "mask-image" ? NoImage : null };
            CssValue result = specified;
            long allocated = AllocationWindow.Measure(1000,
                _ => result = ComputedValueResolver.Resolve(property, specified, context));
            TestContext.WriteLine(property + ": " + allocated + " bytes / 1000 resolutions");
            Assert.That(result.Serialize(), Is.EqualTo("25% 75%"));
            Assert.That(allocated, Is.Zero);
        }
        private static readonly CssKeywordValue NoImage = new("none");

        [TestCase("mask-position", "25% 75%, 10% 90%", "25% 75%, 10% 90%")]
        [TestCase("transform-origin", "25% 75% 3px", "25% 75% 3px")]
        [TestCase("transform-origin", "25% 75% 0px", "25% 75%")]
        public void PositionSharingPreservesLayersAndCanonicalDepth(string property, string text, string expected)
        {
            var source = ComputedPropertyTests.Parse(property, text);
            var result = ComputedValueResolver.Resolve(property, source, default);
            Assert.That(result.Serialize(), Is.EqualTo(expected));
        }

        [Test]
        public void MaskLayerMatchingStillRepeatsTruncatesAndRereadsTheImageProvider()
        {
            var source = ComputedPropertyTests.Parse("mask-position", "25% 75%, 10% 90%");
            CssValue images = ComputedPropertyTests.Parse("mask-image", "none, none");
            var context = new ComputedValueContext { PropertyValue = name => name == "mask-image" ? images : null };
            CssValue result = source;
            long allocated = AllocationWindow.Measure(1000,
                _ => result = ComputedValueResolver.Resolve("mask-position", source, context));
            Assert.That(allocated, Is.Zero);
            Assert.That(result, Is.SameAs(source));
            images = ComputedPropertyTests.Parse("mask-image", "none, none, none");
            Assert.That(ComputedValueResolver.Resolve("mask-position", source, context).Serialize(),
                Is.EqualTo("25% 75%, 10% 90%, 25% 75%"));
            images = NoImage;
            Assert.That(ComputedValueResolver.Resolve("mask-position", source, context).Serialize(), Is.EqualTo("25% 75%"));
        }

        [Test]
        public void SharedPositionRereadsMutableChildrenAndRelativeUnitsInsteadOfCachingAResult()
        {
            var percent = new LivePercentage();
            CssValue[] coordinates = [percent, new CssPercentageValue(75)];
            var source = new CssListValue(coordinates, CssListSeparator.Space);
            var first = ComputedValueResolver.Resolve("object-position", source, default);
            Assert.That(first.Serialize(), Is.EqualTo("25% 75%"));
            percent.Current = 35;
            Assert.That(ComputedValueResolver.Resolve("object-position", source, default).Serialize(), Is.EqualTo("35% 75%"));
            coordinates[0] = new CssDimensionValue(2, "em");
            Assert.That(ComputedValueResolver.Resolve("object-position", source, ComputedUnitTests.Context).Serialize(), Is.EqualTo("40px 75%"));
            Assert.That(ComputedValueResolver.Resolve("object-position", source,
                ComputedUnitTests.Context with { Font = new ComputedFontMetrics(30) }).Serialize(), Is.EqualTo("60px 75%"));
        }
        private sealed class LivePercentage : CssPercentageValue
        {
            internal double Current = 25;
            internal LivePercentage() : base(25) { }
            public override double Value => Current;
        }

        [Test]
        public void PositionAllocationWindowStillCountsActualKeywordConversion()
        {
            var source = ComputedPropertyTests.Parse("object-position", "right bottom");
            CssValue result = source;
            long allocated = AllocationWindow.Measure(1000,
                _ => result = ComputedValueResolver.Resolve("object-position", source, default));
            Assert.That(allocated, Is.GreaterThan(0));
            Assert.That(result.Serialize(), Is.EqualTo("100% 100%"));
        }

        // WPT css/css-values/position/position-computed.tentative.html, as Chrome 155 computes it.
        [TestCase("horizontal-tb", "ltr", "x-start", "0% 50%")]
        [TestCase("horizontal-tb", "rtl", "x-start", "100% 50%")]
        [TestCase("vertical-rl", "ltr", "x-start", "100% 50%")]
        [TestCase("vertical-rl", "rtl", "x-end", "0% 50%")]
        [TestCase("vertical-lr", "rtl", "x-end", "100% 50%")]
        [TestCase("vertical-rl", "rtl", "y-start", "50% 100%")]
        [TestCase("vertical-lr", "ltr", "y-end", "50% 100%")]
        [TestCase("sideways-rl", "rtl", "y-start", "50% 100%")]
        [TestCase("sideways-lr", "ltr", "y-start", "50% 100%")]
        [TestCase("sideways-lr", "rtl", "y-start", "50% 0%")]
        [TestCase("sideways-lr", "ltr", "x-end", "100% 50%")]
        public void FlowRelativePhysicalAxesFollowWritingModeAndDirection(string mode, string direction, string input, string expected)
        {
            var context = new ComputedValueContext
            {
                PropertyValue = name => name == "writing-mode" ? new CssKeywordValue(mode) :
                    name == "direction" ? new CssKeywordValue(direction) : null
            };
            Assert.That(ComputedValueResolver.Resolve("object-position", ComputedPropertyTests.Parse("object-position", input), context).Serialize(),
                Is.EqualTo(expected));
        }
        // WPT css/css-anchor-position/position-area-computed.html ambiguous logical keyword cases.
        [TestCase("start span-all", "block-start")]
        [TestCase("span-all start", "inline-start")]
        [TestCase("self-end span-all", "self-block-end")]
        [TestCase("span-all self-end", "self-inline-end")]
        [TestCase("span-start span-all", "span-block-start")]
        [TestCase("span-all span-self-end", "span-self-inline-end")]
        public void SpanAllDisambiguatesLogicalAxisWithoutInventingAnAnchorBox(string input, string expected)
        {
            Assert.That(ComputedValueResolver.Resolve("position-area", ComputedPropertyTests.Parse("position-area", input), default).Serialize(),
                Is.EqualTo(expected));
        }
    }
}
