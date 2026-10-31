using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssOm;

[TestFixture]
public sealed class ComputedSerializationCacheTests
{
    [Test]
    public void RepeatedIndexedReadReusesTextButStillChecksTheLiveView()
    {
        var style = new CSSStyleDeclaration { ReadOnly = true, IsComputed = true };
        style.ReplaceComputed(CSSStyleDeclaration.Parse("width: 123.5px").EnumerateLonghands());
        int reads = 0;
        style.BeforeRead = () => reads++;
        string first = style.GetPropertyValue("width");
        Assert.That(style.GetPropertyValue("WIDTH"), Is.SameAs(first));
        Assert.That(reads, Is.EqualTo(2), "A text cache must never bypass BeforeRead invalidation.");
    }

    [Test]
    public void ComputedFastPathKeepsAliasesShorthandsAndCustomPropertyCase()
    {
        var style = new CSSStyleDeclaration { IsComputed = true };
        style.ReplaceComputed(CSSStyleDeclaration.Parse(
            "overflow-wrap: break-word; margin: 1px 2px; --Mixed: 12px; --mixed: 13px").EnumerateLonghands());
        Assert.That(style.GetPropertyValue("word-wrap"), Is.EqualTo("break-word"));
        Assert.That(style.GetPropertyValue("margin"), Is.EqualTo("1px 2px"));
        Assert.That(style.GetPropertyValue("--Mixed"), Is.EqualTo("12px"));
        Assert.That(style.GetPropertyValue("--mixed"), Is.EqualTo("13px"));
        Assert.That(style.GetPropertyValue("--MIXED"), Is.Empty);
    }

    [Test]
    public void WarmImmutableReadsAllocateNoSerializationBuffers()
    {
        var style = new CSSStyleDeclaration { IsComputed = true };
        style.ReplaceComputed(CSSStyleDeclaration.Parse("width: 123.5px").EnumerateLonghands());
        for (int i = 0; i < 100; i++) style.GetPropertyValue("width");
        long allocated = AllocationWindow.Measure(1000, _ => style.GetPropertyValue("width"));
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void ReplacementReorderingAndShrinkingNeverReuseStaleText()
    {
        var style = new CSSStyleDeclaration();
        style.ReplaceComputed(CSSStyleDeclaration.Parse("width: 10px; height: 20px").EnumerateLonghands());
        Assert.That(style.GetPropertyValue("width"), Is.EqualTo("10px"));
        Assert.That(style.GetPropertyValue("height"), Is.EqualTo("20px"));
        style.ReplaceComputed(CSSStyleDeclaration.Parse("height: 30px; width: 40px").EnumerateLonghands());
        Assert.That(style.GetPropertyValue("width"), Is.EqualTo("40px"));
        Assert.That(style.GetPropertyValue("height"), Is.EqualTo("30px"));
        style.ReplaceComputed(CSSStyleDeclaration.Parse("height: 50px").EnumerateLonghands());
        Assert.That(style.GetPropertyValue("width"), Is.Empty);
        Assert.That(style.GetPropertyValue("height"), Is.EqualTo("50px"));
    }

    [Test]
    public void HeldComputedViewTracksOwnGeometryAndInheritedColorChanges()
    {
        var document = new Document().Implementation.CreateHTMLDocument();
        var parent = document.CreateElement("div");
        var child = document.CreateElement("div");
        document.Body!.AppendChild(parent); parent.AppendChild(child);
        parent.SetAttribute("style", "color: red"); child.SetAttribute("style", "width: 10px");
        var engine = new StyleEngine(document);
        var view = engine.GetComputedStyle(child);
        Assert.That(view.GetPropertyValue("width"), Is.EqualTo("10px"));
        string red = view.GetPropertyValue("color");
        child.SetAttribute("style", "width: 20px"); parent.SetAttribute("style", "color: blue");
        Assert.That(view.GetPropertyValue("width"), Is.EqualTo("20px"));
        Assert.That(view.GetPropertyValue("color"), Is.Not.EqualTo(red));
    }

    [Test]
    public void MutableTypedSlotsAndTheirCompositeParentsAreNotCached()
    {
        var number = new CSSUnitValue(.5, "number");
        var length = new CSSUnitValue(10, "px");
        var numberSlot = new TypedNumberSlot(number);
        var lengthSlot = new TypedDimensionSlot(length);
        var style = new CSSStyleDeclaration { IsComputed = true };
        style.ReplaceComputed(new[] {
            new CssDeclaration("opacity", numberSlot, false),
            new CssDeclaration("--pair", new CssListValue(new CssValue[] {
                lengthSlot, new CssDimensionValue(20, "px") }, CssListSeparator.Space), false)
        });
        Assert.That(style.GetPropertyValue("opacity"), Is.EqualTo("0.5"));
        Assert.That(style.GetPropertyValue("--pair"), Is.EqualTo("10px 20px"));
        numberSlot.Current.Value = .8; lengthSlot.Current.Value = 30;
        Assert.That(style.GetPropertyValue("opacity"), Is.EqualTo("0.8"));
        Assert.That(style.GetPropertyValue("--pair"), Is.EqualTo("30px 20px"));
    }

    [Test]
    public void IndexedViewReturningToOrdinaryMutationDoesNotUseItsOldCache()
    {
        var style = new CSSStyleDeclaration();
        style.ReplaceComputed(CSSStyleDeclaration.Parse("width: 10px").EnumerateLonghands());
        Assert.That(style.GetPropertyValue("width"), Is.EqualTo("10px"));
        style.SetProperty("width", "20px");
        Assert.That(style.GetPropertyValue("width"), Is.EqualTo("20px"));
        style.CssText = "height: 30px";
        Assert.That(style.GetPropertyValue("width"), Is.Empty);
        Assert.That(style.GetPropertyValue("height"), Is.EqualTo("30px"));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(99)]
    public void UnchangedComputedDeclarationsKeepStorageAndOnlyChangedScalarsLoseText(int ordinal)
    {
        var style = new CSSStyleDeclaration { IsComputed = true };
        style.ReplaceComputed(new[] { new CssDeclaration("width", new CssDimensionValue(123.5, "px"), false),
            new CssDeclaration("opacity", new CssNumberValue(.5, false), false) });
        var original = style.Declarations[0];
        string text = style.GetPropertyValue("width");
        var unchanged = style.ReuseComputedDeclaration(ordinal, "width", new CssDimensionValue(123.5, "px"));
        style.ReplaceComputed(new[] { unchanged, style.ReuseComputedDeclaration(1, "opacity", new CssNumberValue(.8, false)) });
        Assert.That(style.Declarations[0], Is.SameAs(original));
        Assert.That(style.GetPropertyValue("width"), Is.SameAs(text));
        Assert.That(style.GetPropertyValue("opacity"), Is.EqualTo("0.8"));
    }

    [Test]
    public void EqualButDistinctMutableGraphsAreNeverReused()
    {
        var first = new TypedDimensionSlot(new CSSUnitValue(10, "px"));
        var second = new TypedDimensionSlot(new CSSUnitValue(10, "px"));
        var style = new CSSStyleDeclaration { IsComputed = true };
        var prior = new CssDeclaration("--pair", new CssListValue(new CssValue[] { first }, CssListSeparator.Space), false);
        style.ReplaceComputed(new[] { prior });
        var next = style.ReuseComputedDeclaration(0, "--pair", new CssListValue(new CssValue[] { second }, CssListSeparator.Space));
        Assert.That(next, Is.Not.SameAs(prior));
        style.ReplaceComputed(new[] { next });
        second.Current.Value = 30;
        first.Current.Value = 99;
        Assert.That(style.GetPropertyValue("--pair"), Is.EqualTo("30px"));
    }

    [Test]
    public void DeclarationReuseDoesNotCollapseDifferentNumericBitPatternsOrUrlMetadata()
    {
        var style = new CSSStyleDeclaration { IsComputed = true };
        var zero = new CssDeclaration("width", new CssDimensionValue(-0.0, "px"), false);
        var url = new CssDeclaration("background-image", new CssUrlValue("res:///same.png", integrity: "first"), false);
        style.ReplaceComputed(new[] { zero, url });
        Assert.That(style.ReuseComputedDeclaration(0, "width", new CssDimensionValue(0.0, "px")), Is.Not.SameAs(zero));
        Assert.That(style.ReuseComputedDeclaration(1, "background-image", new CssUrlValue("res:///same.png", integrity: "second")),
            Is.Not.SameAs(url));
    }

    [Test]
    [NonParallelizable]
    public void OrdinaryLonghandResolutionAllocatesNoUnrelatedContextClosures()
    {
        var value = new CssKeywordValue("visible");
        var (allocated, result) = MeasureResolution("visibility", value);
        Assert.That(allocated, Is.Zero);
        Assert.That(result, Is.SameAs(value));
    }

    [Test]
    [NonParallelizable]
    public void WarmNumericZeroResolutionDoesNotReparsePropertyTypeGrammars()
    {
        var value = new CssNumberValue(0, false);
        var (allocated, result) = MeasureResolution("opacity", value);
        Assert.That(allocated, Is.Zero);
        Assert.That(result, Is.SameAs(value));
    }

    // A scalar does not
    // need a temporary one-item collection to enter the same property normalizer as a list.
    [TestCase("align-items", "normal")]
    [TestCase("align-content", "normal")]
    [TestCase("align-self", "auto")]
    [TestCase("justify-content", "normal")]
    [TestCase("border-image-repeat", "stretch")]
    [TestCase("mask-border-repeat", "stretch")]
    [TestCase("position-area", "none")]
    [TestCase("scroll-snap-align", "none")]
    [TestCase("scroll-snap-type", "none")]
    [NonParallelizable]
    public void ScalarNormalizationDoesNotAllocateASingletonArray(string property, string keyword)
    {
        var value = new CssKeywordValue(keyword);
        var (allocated, result) = MeasureResolution(property, value);
        TestContext.WriteLine(property + ": " + allocated + " bytes / 1024 resolutions");
        Assert.That(result, Is.SameAs(value));
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void BorrowedItemViewKeepsMutableSlotsListOrderAndNonSpaceSeparatorsLive()
    {
        // CSS Overflow 3: padding-box is omitted, other visual boxes precede the length.
        var slot = new TypedDimensionSlot(new CSSUnitValue(10, "px"));
        Assert.That(ComputedValueResolver.Resolve("overflow-clip-margin", slot, default), Is.SameAs(slot));
        slot.Current.Value = 20;
        Assert.That(ComputedValueResolver.Resolve("overflow-clip-margin", slot, default).Serialize(), Is.EqualTo("20px"));
        CssValue[] values = [new CssKeywordValue("content-box"), slot];
        var space = new CssListValue(values, CssListSeparator.Space);
        Assert.That(ComputedValueResolver.Resolve("overflow-clip-margin", space, default).Serialize(), Is.EqualTo("content-box 20px"));
        values[0] = new CssKeywordValue("padding-box");
        Assert.That(ComputedValueResolver.Resolve("overflow-clip-margin", space, default), Is.SameAs(slot));
        foreach (var separator in new[] { CssListSeparator.Comma, CssListSeparator.Slash })
        {
            var grouped = new CssListValue(values, separator);
            Assert.That(ComputedValueResolver.Resolve("overflow-clip-margin", grouped, default), Is.SameAs(grouped),
                "Only space-separated values are flattened; other separators remain one scalar.");
        }
    }

    [Test]
    [NonParallelizable]
    public void ResolutionAllocationWindowStillDetectsAnAllocatingConversion()
    {
        // Negative control for the exact-zero measurements (ComputedValueResolver.Fonts.cs FontValue):
        // font-weight:bold creates a numeric 700 value; measurement preparation must not hide real allocations.
        var (allocated, result) = MeasureResolution("font-weight", new CssKeywordValue("bold"));
        Assert.That(allocated, Is.GreaterThan(0));
        Assert.That(result, Is.TypeOf<CssNumberValue>());
        Assert.That(((CssNumberValue)result!).Value, Is.EqualTo(700));
    }

    // CSSOM 1 § 6.6.1, CSS Values 4 lists.
    [TestCase(CssListSeparator.Space, 1000, "1px auto")]
    [TestCase(CssListSeparator.Comma, 1000, "1px, auto")]
    [TestCase(CssListSeparator.Slash, 1000, "1px / auto")]
    [TestCase(CssListSeparator.Space, 1024, "1px auto")]
    [TestCase(CssListSeparator.Comma, 1024, "1px, auto")]
    [TestCase(CssListSeparator.Slash, 1024, "1px / auto")]
    public void WarmFlatComputedListReadsAllocateNoSerializationBuffers(CssListSeparator separator, int count, string expected)
    {
        var style = FlatStyle(new CssValue[] { new CssDimensionValue(1, "px"), new CssKeywordValue("auto") }, separator);
        string first = style.GetPropertyValue("--list");
        long allocated = AllocationWindow.Measure(count, _ => style.GetPropertyValue("--list"));
        TestContext.WriteLine(separator + ": " + allocated + " bytes / " + count + " reads");
        Assert.That(first, Is.EqualTo(expected));
        Assert.That(allocated, Is.Zero);
        Assert.That(style.GetPropertyValue("--list"), Is.SameAs(first));
    }

    [Test]
    public void FlatListTextTracksArrayEditsAndBeforeRead()
    {
        CssValue[] items = [new CssDimensionValue(1, "px"), new CssKeywordValue("auto")];
        var style = FlatStyle(items);
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("1px auto"));
        items[0] = new CssDimensionValue(2, "px");
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("2px auto"));
        int reads = 0;
        style.BeforeRead = () => { reads++; items[1] = new CssKeywordValue("none"); };
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("2px none"));
        Assert.That(reads, Is.EqualTo(1));
    }

    [Test]
    public void FlatListTextTracksListResizeReorderingAndReplacement()
    {
        var items = new System.Collections.Generic.List<CssValue> { new CssKeywordValue("a"), new CssKeywordValue("b") };
        var style = FlatStyle(items);
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("a b"));
        items.Reverse();
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("b a"));
        items.RemoveAt(1);
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("b"));
        items.Add(new CssKeywordValue("c"));
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("b c"));
        style.ReplaceComputed(new[] { new CssDeclaration("--list",
            new CssListValue(new CssValue[] { new CssKeywordValue("d") }, CssListSeparator.Comma), false) });
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("d"));
        items.Clear();
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("d"));
        style.ReplaceComputed(CSSStyleDeclaration.Parse("--list: scalar").EnumerateLonghands());
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("scalar"));
        style.CssText = "--list: ordinary";
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("ordinary"));
    }

    [Test]
    public void FlatListCacheRejectsMutableNumericSlotsAndNestedLists()
    {
        var number = new TypedNumberSlot(new CSSUnitValue(1, "number"));
        CssValue[] nestedItems = [new CssKeywordValue("a")];
        CssValue[] items = [new CssKeywordValue("flat")];
        var style = FlatStyle(items);
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("flat"));
        items[0] = number;
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("1"));
        number.Current.Value = 2;
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("2"));
        items[0] = new CssListValue(nestedItems, CssListSeparator.Space);
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("a"));
        nestedItems[0] = new CssKeywordValue("b");
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("b"));
        items[0] = new CssKeywordValue("flat-again");
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("flat-again"));
    }

    [Test]
    public void FlatListCacheRejectsRootSubclassCustomContainerAndDerivedSerializer()
    {
        var derived = new LiveList();
        var style = new CSSStyleDeclaration { IsComputed = true };
        style.ReplaceComputed(new[] { new CssDeclaration("--list", derived, false) });
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("first"));
        derived.Text = "second";
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("second"));
        var custom = new LiveItems();
        style = FlatStyle(custom);
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("1"));
        custom.Value = new CssNumberValue(2, false);
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("2"));
        var leaf = new LiveNumber();
        style = FlatStyle(new CssValue[] { leaf });
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("3"));
        leaf.Current = 4;
        Assert.That(style.GetPropertyValue("--list"), Is.EqualTo("4"));
    }

    private static CSSStyleDeclaration FlatStyle(System.Collections.Generic.IReadOnlyList<CssValue> items,
        CssListSeparator separator = CssListSeparator.Space)
    {
        var style = new CSSStyleDeclaration { IsComputed = true };
        style.ReplaceComputed(new[] { new CssDeclaration("--list", new CssListValue(items, separator), false) });
        return style;
    }
    private sealed class LiveList() : CssListValue(System.Array.Empty<CssValue>(), CssListSeparator.Space)
    {
        internal string Text = "first";
        public override void AppendTo(System.Text.StringBuilder sb) => sb.Append(Text);
    }
    private sealed class LiveNumber() : CssNumberValue(0, false)
    {
        internal double Current = 3;
        public override double Value => Current;
    }
    private sealed class LiveItems : System.Collections.Generic.IReadOnlyList<CssValue>
    {
        internal CssValue Value = new CssNumberValue(1, false);
        public int Count => 1;
        public CssValue this[int index] => Value;
        public System.Collections.Generic.IEnumerator<CssValue> GetEnumerator() { yield return Value; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private const int ResolutionSampleCount = 1024;
    private static (long Allocated, CssValue? Result) MeasureResolution(string property, CssValue value)
    {
        // Every one of the sampled calls stays inside the allocation counters.
        CssValue? result = null;
        long allocated = AllocationWindow.Measure(ResolutionSampleCount, _ => result = ComputedValueResolver.Resolve(property, value, default));
        return (allocated, result);
    }
}
