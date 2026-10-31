using System;
using NecronomiDom.Layout;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class FlexLineSolverTests
{
    [Test]
    public void GrowRedistributesAfterMaximumFreeze()
    {
        var items = new[] { Item(40, grow: 1, max: 60), Item(40, grow: 1) };
        var result = Resolve(items, 200);
        Assert.That(result[0].Size, Is.EqualTo(60));
        Assert.That(result[1].Size, Is.EqualTo(140));
    }
    [Test]
    public void ScaledShrinkRedistributesAfterMinimumFreeze()
    {
        var items = new[] { Item(100, min: 90), Item(200) };
        var result = Resolve(items, 210);
        Assert.That(result[0].Size, Is.EqualTo(90));
        Assert.That(result[1].Size, Is.EqualTo(120));
    }
    [Test]
    public void ShrinkZeroRetainsAuthoredStageEvenWhenLineOverflows()
    {
        var result = Resolve(new[] { Item(120, shrink: 0), Item(40, shrink: 0) }, 100, "center");
        Assert.That(result[0].Size, Is.EqualTo(120));
        Assert.That(result[0].Offset, Is.EqualTo(-30));
        Assert.That(result[1].Offset, Is.EqualTo(90));
    }
    [Test]
    public void PartialGrowLeavesUnclaimedSpaceForAlignment()
    {
        var result = Resolve(new[] { Item(40, grow: .25) }, 200, "center");
        Assert.That(result[0].Size, Is.EqualTo(80));
        Assert.That(result[0].Offset, Is.EqualTo(60));
    }
    [Test]
    public void BordersDoNotContributeToScaledShrinkWeight()
    {
        var items = new[] { Item(100) with { Decoration = 40 }, Item(100) };
        var result = Resolve(items, 200);
        Assert.That(result[0].Size, Is.EqualTo(80));
        Assert.That(result[1].Size, Is.EqualTo(80));
        Assert.That(result[1].Offset, Is.EqualTo(120));
    }
    [Test]
    public void AutoMarginsConsumeFreeSpaceBeforeJustification()
    {
        var items = new[] { Item(40, shrink: 0) with { AutoStart = true }, Item(40, shrink: 0) };
        var result = Resolve(items, 200, "space-between");
        Assert.That(result[0].Offset, Is.EqualTo(120));
        Assert.That(result[1].Offset, Is.EqualTo(160));
    }
    [Test]
    public void ReverseUsesLogicalMarginsAndBorderBoxSize()
    {
        var items = new[] { Item(40, shrink: 0) with { MarginStart = 10, MarginEnd = 5, Decoration = 4 }, Item(30, shrink: 0) };
        var result = new FlexMainResult[2];
        FlexLineSolver.Resolve(items, result, 200, 6, "flex-start", true);
        Assert.That(result[0].Offset, Is.EqualTo(146));
        Assert.That(result[1].Offset, Is.EqualTo(105));
    }
    [TestCase("flow-start", 0, 40, 80)]
    [TestCase("flow-end", 80, 120, 160)]
    [TestCase("space-between", 0, 80, 160)]
    [TestCase("space-around", 13.328125, 80, 146.65625)]
    [TestCase("space-evenly", 20, 80, 140)]
    public void HeaderAndControlsDistributeSpace(string justify, double a, double b, double c)
    {
        var result = Resolve(new[] { Item(40), Item(40), Item(40) }, 200, justify);
        Assert.That(result[0].Offset, Is.EqualTo(a).Within(1e-9));
        Assert.That(result[1].Offset, Is.EqualTo(b).Within(1e-9));
        Assert.That(result[2].Offset, Is.EqualTo(c).Within(1e-9));
    }
    [TestCase(true, 33.328125, 33.34375, 33.328125)]
    [TestCase(false, 33.34375, 33.328125, 33.328125)]
    public void FractionalGrowAndScaledShrinkConserveEveryLayoutUnit(bool grow, double a, double b, double c)
    {
        var item = Item(grow ? 0 : 100, grow: grow ? 1 : 0);
        var result = Resolve(new[] { item, item, item }, 100);
        Assert.That(new[] { result[0].Size, result[1].Size, result[2].Size }, Is.EqualTo(new[] { a, b, c }));
        Assert.That(result[2].Offset + result[2].Size, Is.EqualTo(100));
    }
    [Test]
    public void QuantizedHypotheticalSizesFreezeAndRedistributeRemainingFreeSpace()
    {
        var result = Resolve(new[] { Item(40.011, min: 50.021, grow: 1), Item(40.011, grow: 2) }, 100.02);
        Assert.That(result[0].Size, Is.EqualTo(50.015625));
        Assert.That(result[1].Size, Is.EqualTo(50));
        Assert.That(result[1].Offset + result[1].Size, Is.EqualTo(100.015625));
    }
    [Test]
    public void AutoMarginsDiffuseIndivisibleLayoutUnitsWithoutLeavingAnEndGap()
    {
        var item = Item(10, shrink: 0) with { AutoStart = true };
        var result = Resolve(new[] { item, item, item }, 31);
        Assert.That(new[] { result[0].MarginStart, result[1].MarginStart, result[2].MarginStart },
            Is.EqualTo(new[] { .328125, .34375, .328125 }));
        Assert.That(result[2].Offset + result[2].Size, Is.EqualTo(31));
    }
    [Test]
    public void JustificationDiffusesItsRemainderAndKeepsTheFinalBorderFlush()
    {
        var result = Resolve(new[] { Item(10, shrink: 0), Item(10, shrink: 0), Item(10, shrink: 0) }, 31.015625, "space-between");
        Assert.That(new[] { result[0].Offset, result[1].Offset, result[2].Offset }, Is.EqualTo(new[] { 0d, 10.515625, 21.015625 }));
        Assert.That(result[2].Offset + result[2].Size, Is.EqualTo(31.015625));
    }
    [Test]
    [NonParallelizable]
    public void FixedPointDistributionStillAllocatesNothingOnCallerOwnedSpans()
    {
        var (allocated, result) = MeasureSpaceAround(freshResults: false);
        Assert.That(allocated, Is.Zero);
        Assert.That(new[] { result[0].Size, result[1].Size, result[2].Size }, Is.EqualTo(new[] { 33.328125, 33.34375, 33.328125 }));
    }
    [Test]
    [NonParallelizable]
    public void FixedPointAllocationWindowStillDetectsPerCallResultArrays()
    {
        // A fresh results array per call is the real allocating alternative to caller-owned spans;
        // the same preparation and counter window must not hide it.
        var (allocated, _) = MeasureSpaceAround(freshResults: true);
        Assert.That(allocated, Is.GreaterThan(0));
    }
    private const int SpaceAroundSampleCount = 100;
    private static (long Allocated, FlexMainResult[] Result) MeasureSpaceAround(bool freshResults)
    {
        var items = new[] { Item(0, grow: 1), Item(0, grow: 1), Item(0, grow: 1) };
        var result = new FlexMainResult[items.Length];
        long allocated = AllocationWindow.Measure(SpaceAroundSampleCount, _ =>
            FlexLineSolver.Resolve(items, freshResults ? new FlexMainResult[items.Length] : result, 100, 0, "space-around"));
        return (allocated, result);
    }
    [Test]
    public void UnsupportedJustificationRefusesRatherThanFabricatingGeometry()
        => Assert.Throws<NotSupportedException>(() => Resolve(new[] { Item(40) }, 200, "safe center"));
    private static FlexMainItem Item(double basis, double min = 0, double max = double.PositiveInfinity, double grow = 0, double shrink = 1)
        => new(basis, min, max, grow, shrink);
    private static FlexMainResult[] Resolve(FlexMainItem[] items, double available, string justify = "flex-start")
    {
        var result = new FlexMainResult[items.Length];
        FlexLineSolver.Resolve(items, result, available, 0, justify);
        return result;
    }
}
