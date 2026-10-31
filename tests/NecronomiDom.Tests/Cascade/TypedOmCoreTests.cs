using System;
using System.Collections.Generic;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade
{
    [TestFixture]
    public sealed class TypedOmCoreTests
    {
        [TestCaseSource(nameof(NumericMeasurementRuns))]
        [Parallelizable(ParallelScope.Children)]
        public void WarmNumericWritesAllocateNothingAndReflectRealStyleAttribute(int repetition)
        {
            var element = Element();
            var map = element.AttributeStyleMap;
            for (int i = 0; i < 128; i++) map.Set("width", new CSSUnitValue(i, "px"));
            // Fixed setup only: never retry or discard a measured result because it allocated.
            long allocated = MeasureNumericWrites(map);
            TestContext.WriteLine($"numeric measurement run={repetition} bytes={allocated} thread={Environment.CurrentManagedThreadId}");
            Assert.That(allocated, Is.Zero);
            Assert.That(element.GetAttribute("style"), Is.EqualTo("width: 255px;"));
        }
        [Test]
        public void NumericMeasurementStillDetectsRealOldValueObserverAllocations()
        {
            var element = Element();
            var map = element.AttributeStyleMap;
            var observer = new MutationObserver((_, _) => { });
            observer.Observe(element, new MutationObserverInit { Attributes = true, AttributeOldValue = true });
            _ = observer.TakeRecords();
            long allocated = MeasureNumericWrites(map);
            Assert.That(allocated, Is.GreaterThan(0));
            // Every write of the warm-up and measured windows is a real change with an old-value record.
            Assert.That(observer.TakeRecords(), Has.Count.EqualTo((AllocationWindow.DefaultWarmupWindows + 1) * 128));
            Assert.That(element.GetAttribute("style"), Is.EqualTo("width: 255px;"));
        }
        [Test]
        public void WarmTransformWritesAllocateNothingAndOwnTheirSnapshot()
        {
            var element = Element();
            var components = new[] { CSSTransformComponent.Translate(new CSSUnitValue(1, "px"), new CSSUnitValue(2, "px")) };
            var value = new CSSTransformValue(components);
            var map = element.AttributeStyleMap;
            for (int i = 0; i < 128; i++)
            { components[0] = CSSTransformComponent.Translate(new CSSUnitValue(i, "px"), new CSSUnitValue(2, "px")); map.Set("transform", value); }
            long allocated = AllocationWindow.Measure(128, i =>
            { components[0] = CSSTransformComponent.Translate(new CSSUnitValue(128 + i, "px"), new CSSUnitValue(2, "px")); map.Set("transform", value); });
            Assert.That(allocated, Is.Zero);
            var read = map.Get("transform")!.Value;
            Assert.That(read.TryGetTransform(out var snapshot), Is.True);
            components[0] = CSSTransformComponent.Scale(9, 9);
            Assert.That(element.Style.GetPropertyValue("transform"), Is.EqualTo("translate(255px, 2px)"));
            map.Set("transform", value);
            Assert.That(snapshot.ToString(), Is.EqualTo("translate(255px, 2px)"));
        }
        [Test]
        public void NumericReadIsSnapshotAndRelativeConversionNeedsNoFabricatedBasis()
        {
            var element = Element();
            element.AttributeStyleMap.Set("width", new CSSUnitValue(10, "px"));
            var prior = element.AttributeStyleMap.Get("width")!.Value;
            element.AttributeStyleMap.Set("width", new CSSUnitValue(20, "px"));
            Assert.That(prior.ToString(), Is.EqualTo("10px"));
            Assert.That(CSSNumericValue.Parse("calc(2em + 3em)").To("em").Value, Is.EqualTo(5));
            Assert.That(CSSNumericValue.Parse("calc(2px + 3px)").Equals(new CSSUnitValue(5, "px")), Is.True);
            Assert.Throws<DomTypeError>(() => CSSNumericValue.Parse("calc(1px + 1%)").To("px"));
        }
        [Test]
        public void UnparsedUsesSharedTokenBoundariesAndKeepsNestedVariableObjects()
        {
            Assert.That(new CSSUnparsedValue(new CSSUnparsedSegment[] { "lem", "on", "ade" }).ToString(), Is.EqualTo("lem/**/on/**/ade"));
            var fallback = new CSSUnparsedValue(new CSSUnparsedSegment[] { new CSSVariableReferenceValue("--b") });
            var reference = new CSSVariableReferenceValue("--a", fallback);
            Assert.That(new CSSUnparsedValue(new CSSUnparsedSegment[] { reference, reference }).ToString(), Is.EqualTo("var(--a,var(--b))var(--a,var(--b))"));
            var parsed = CSSStyleValue.Parse("width", "calc(1px + var(--gap))");
            Assert.That(parsed.TryGetUnparsed(out var unparsed), Is.True);
            Assert.That(unparsed!.Count, Is.EqualTo(3));
            Assert.That(unparsed[1].Variable!.Variable, Is.EqualTo("--gap"));
            unparsed[unparsed.Count] = " ";
            Assert.That(unparsed.Count, Is.EqualTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => unparsed[unparsed.Count + 1] = "x");
        }
        [Test]
        public void EscapedVariableReferenceKeepsCookedNameIdentity()
        {
            var parsed = CSSStyleValue.Parse("--unparsed", "var(--a\\,fail)");
            Assert.That(parsed.TryGetUnparsed(out var value), Is.True);
            Assert.That(value![0].Variable!.Variable, Is.EqualTo("--a,fail"));
            Assert.That(value.ToString(), Is.EqualTo("var(--a\\,fail)"));
        }
        private static IEnumerable<int> NumericMeasurementRuns()
        {
            for (int i = 1; i <= 50; i++) yield return i;
        }
        private static long MeasureNumericWrites(StylePropertyMap map)
            => AllocationWindow.Measure(128, i => map.Set("width", new CSSUnitValue(128 + i, "px")));
        private static HTMLElement Element()
        {
            var document = new Document().Implementation.CreateHTMLDocument("typed");
            _ = document.DefaultView;
            var element = (HTMLElement)document.CreateElement("div");
            document.Body!.AppendChild(element);
            return element;
        }
    }
}
