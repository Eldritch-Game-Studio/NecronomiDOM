using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

// Incremental reconciliation must not change the full cascade result; CSS Values 4 §6.1.1 defines the font-relative bases.
// CSS Typed OM 1 §6 requires notifying map writes; StylePropertyMap owns reusable mutable numeric slots.
[TestFixture]
public sealed class StyleComputedReuseTests
{
    [Test]
    public void InternalReusePipelineBorrowsTheLargeReadonlyContextButPublicResolutionKeepsItsSnapshotBoundary()
    {
        // The large computed-value context is passed by readonly reference; this signature contract
        // is checked structurally rather than as a timing assertion.
        AssertContext(typeof(StyleEngine), "ComputeRemainingValues");
        AssertContext(typeof(StyleEngine), "ComputeOrReuseValue");
        AssertContext(typeof(StyleEngine), "ComputeTextColor");
        Type cache = typeof(StyleEngine).GetNestedType("ComputedReuse", BindingFlags.NonPublic)!;
        AssertContext(cache, "Admit");
        AssertContext(cache, "SameContext", 2);
        AssertContext(typeof(ComputedValueResolver), "Normalize");
        AssertContext(typeof(ComputedValueResolver), "MapList");
        AssertContext(typeof(ComputedValueResolver), "Canonical");
        AssertContext(typeof(ComputedValueResolver), "PropertyValue");
        Assert.That(typeof(ComputedValueResolver).GetMethod(nameof(ComputedValueResolver.Resolve))!
            .GetParameters()[2].ParameterType, Is.EqualTo(typeof(ComputedValueContext)),
            "public callers retain a value snapshot even if a provider reassigns their original context variable");
        TestContext.Progress.WriteLine("ComputedValueContext managed size (diagnostic): " + Unsafe.SizeOf<ComputedValueContext>() + " bytes");

        static void AssertContext(Type owner, string name, int expected = 1)
        {
            var method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
            var parameters = method.GetParameters().Where(parameter => parameter.ParameterType == typeof(ComputedValueContext)
                || parameter.ParameterType.GetElementType() == typeof(ComputedValueContext)).ToArray();
            Assert.That(parameters, Has.Length.EqualTo(expected), owner.Name + "." + name);
            foreach (var parameter in parameters)
                Assert.That(parameter.ParameterType.IsByRef && parameter.IsIn, Is.True, owner.Name + "." + name + ":" + parameter.Name);
        }
    }

    [Test]
    public void PublicResolutionSnapshotSurvivesProviderReassignmentOfTheCallersContextVariable()
    {
        // C# value-parameter semantics and CSS Text 3 match-parent: the two parent reads share one input snapshot.
        ComputedValueContext context = default;
        context = new ComputedValueContext
        {
            ParentPropertyValue = name =>
            {
                if (name == "text-align")
                {
                    context = context with { ParentPropertyValue = _ => new CssKeywordValue("ltr") };
                    return new CssKeywordValue("start");
                }
                return new CssKeywordValue("rtl");
            }
        };
        Assert.That(ComputedValueResolver.Resolve("text-align", new CssKeywordValue("match-parent"), context).Serialize(),
            Is.EqualTo("right"));
        Assert.That(context.ParentPropertyValue!("direction")!.Serialize(), Is.EqualTo("ltr"),
            "the callback really reassigns the caller's context, not the resolver's owned snapshot");
    }

    [TestCase("var(--source)")]
    [TestCase("var(--missing,var(--source))")]
    public void PaletteRefreshRetainsRetokenizedInheritedAliasAcrossBothProjections(string expression)
    {
        // Custom Properties 1 §§2–3:
        // inherited variables are substituted in their defining scope, not recomputed in the consumer.
        string css = "html{--source:rgb(3 5 7)}#p{--stable:" + expression +
            ";background-color:var(--paint)}#c{color:var(--stable)}#g{--from-alias:var(--stable)}";
        var (doc, styles) = Load(true, css); var (control, reference) = Load(false, css);
        Compare(doc, styles, control, reference);
        var parent = (HTMLElement)doc.GetElementById("p")!;
        var other = (HTMLElement)control.GetElementById("p")!;
        CssValue stable = styles.GetComputedValues(parent).GetSpecifiedValue("--stable")!;
        CssValue descendant = styles.GetComputedValues(doc.GetElementById("g")!).GetSpecifiedValue("--from-alias")!;
        Assert.That(stable, Is.Not.SameAs(styles.GetComputedValues(doc.DocumentElement!).GetSpecifiedValue("--source")),
            "reuse the retokenized projection, not the parent's original token graph");
        for (int i = 0; i < 16; i++)
        {
            string color = $"rgb({i} 11 13)";
            parent.Style.SetProperty("--paint", color); other.Style.SetProperty("--paint", color);
            Compare(doc, styles, control, reference);
            Assert.That(styles.GetComputedStyle(parent).GetSpecifiedValue("--stable"), Is.SameAs(stable));
            Assert.That(styles.GetComputedValues(parent).GetSpecifiedValue("--stable"), Is.SameAs(stable));
            Assert.That(styles.GetComputedValues(doc.GetElementById("g")!).GetSpecifiedValue("--from-alias"), Is.SameAs(descendant));
        }
        ((HTMLElement)doc.DocumentElement!).Style.SetProperty("--source", "rgb(17 19 23)");
        ((HTMLElement)control.DocumentElement!).Style.SetProperty("--source", "rgb(17 19 23)");
        Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedValues(parent).GetSpecifiedValue("--stable"), Is.Not.SameAs(stable));
    }

    [TestCase("  10em  ")]
    [TestCase("\" spaced \"")]
    [TestCase("url('../image.png')")]
    [TestCase("rgb(1 2 3 / .5)")]
    [TestCase("")]
    public void AliasReuseKeepsTheExistingRetokenizationAndTrim(string source)
    {
        // Custom Properties 1 §3: reuse keeps the existing retokenization and trimming.
        var (doc, styles) = Load(true, "#p{--alias:var(--source)}");
        var (control, reference) = Load(false, "#p{--alias:var(--source)}");
        ((HTMLElement)doc.DocumentElement!).Style.CssText = "--source:" + source + ";";
        ((HTMLElement)control.DocumentElement!).Style.CssText = "--source:" + source + ";";
        Compare(doc, styles, control, reference);
        var sourceValue = styles.GetComputedValues(doc.DocumentElement!).GetSpecifiedValue("--source")!;
        string retokenized = new CssUnparsedValue(CssParser.ParseComponentValueList(sourceValue.Serialize())).Serialize();
        var alias = styles.GetComputedValues(doc.GetElementById("p")!).GetSpecifiedValue("--alias")!;
        Assert.That(alias.Serialize(), Is.EqualTo(retokenized));
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("--paint", "blue");
        ((HTMLElement)control.GetElementById("p")!).Style.SetProperty("--paint", "blue");
        Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedValues(doc.GetElementById("p")!).GetSpecifiedValue("--alias"), Is.SameAs(alias));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InheritedAliasSyntaxNeverCachesConsumerFontOrRegisteredComputedLengths(bool registered)
    {
        // Custom Properties 1 §3; Properties and Values API 1 registered computation;
        // Values 4 §6.1.1 font-relative units remain consumer-dependent for unregistered syntax.
        string css = (registered ? "@property --basis{syntax:'<length>';inherits:true;initial-value:1px}" : "") +
            "html{--basis:10em}#p{--stable:var(--basis)}#c{width:var(--stable)}";
        var (doc, styles) = Load(true, css); var (control, reference) = Load(false, css);
        Compare(doc, styles, control, reference);
        var child = (HTMLElement)doc.GetElementById("c")!;
        var other = (HTMLElement)control.GetElementById("c")!;
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("width"), Is.EqualTo(registered ? "160px" : "200px"));
        child.Style.SetProperty("font-size", "30px"); other.Style.SetProperty("font-size", "30px");
        child.Style.SetProperty("--basis", "2px"); other.Style.SetProperty("--basis", "2px");
        Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("width"), Is.EqualTo(registered ? "160px" : "300px"));
        ((HTMLElement)doc.DocumentElement!).Style.SetProperty("--basis", "20em");
        ((HTMLElement)control.DocumentElement!).Style.SetProperty("--basis", "20em");
        Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("width"), Is.EqualTo(registered ? "320px" : "600px"));
    }

    private static (Document Doc, StyleEngine Styles) Load(bool incremental, string extra = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html{font-size:16px;color:black}" +
            "#p{--paint:red;font-size:20px;color:navy;display:flex;align-items:stretch;container-type:size;width:200px;height:100px}" +
            "#c{display:inline;background-color:var(--paint);width:calc(1em + 10px);border-left:medium solid currentcolor;" +
            "text-align:match-parent;text-shadow:1px 2px currentcolor;-webkit-text-stroke-color:currentcolor}" +
            "#g{color:green;-webkit-text-stroke-color:inherit;text-shadow:inherit}" + extra +
            "</style><div id=p><div id=c><span id=g>paint</span></div></div>",
            "computed-reuse", WebUrl.Parse("res:///computed-reuse.html")!).Document;
        return (doc, new StyleEngine(doc) { IncrementalReconciliation = incremental });
    }
    private static void Compare(Document a, StyleEngine actual, Document b, StyleEngine reference)
    {
        foreach (var node in a.QuerySelectorAll("[id]"))
        {
            var element = (Element)node;
            Element other = b.GetElementById(element.Id)!;
            foreach (bool computed in new[] { false, true })
            {
                var left = computed ? actual.GetComputedValues(element) : actual.GetComputedStyle(element);
                var right = computed ? reference.GetComputedValues(other) : reference.GetComputedStyle(other);
                Assert.That(left.Length, Is.EqualTo(right.Length));
                for (int i = 0; i < right.Length; i++)
                {
                    string name = right.Item(i);
                    Assert.That(left.Item(i), Is.EqualTo(name), element.Id + ":order");
                    Assert.That(left.GetPropertyValue(name), Is.EqualTo(right.GetPropertyValue(name)), element.Id + ":" + name);
                }
            }
        }
    }
    [Test]
    public void PaintAndGeometryDeltasActuallyReuseScalarsWithoutLosingInheritedColorExpressions()
    {
        var (doc, styles) = Load(true); var (control, reference) = Load(false);
        Compare(doc, styles, control, reference);
        for (int i = 0; i < 6; i++)
        {
            foreach (var document in new[] { doc, control })
            {
                ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", i % 2 == 0 ? "blue" : "red");
                ((HTMLElement)document.GetElementById("c")!).Style.SetProperty("clip-path", $"polygon(0px 0px,{20+i}px 0px,20px 20px)");
            }
            reference.Invalidate(); Compare(doc, styles, control, reference);
        }
        Assert.That(styles.ReusedComputedValueCount, Is.GreaterThan(0));
    }
    [TestCase("c", "position", "absolute")]
    [TestCase("c", "border-left-style", "none")]
    [TestCase("c", "overflow-y", "hidden")]
    [TestCase("c", "font-size", "30px")]
    [TestCase("c", "color", "purple")]
    [TestCase("p", "align-items", "end")]
    [TestCase("p", "text-align", "right")]
    [TestCase("p", "direction", "rtl")]
    public void CrossPropertyAndInheritedBasisChangesMatchFullComputation(string id, string property, string value)
    {
        var (doc, styles) = Load(true); var (control, reference) = Load(false);
        Compare(doc, styles, control, reference);
        foreach (var document in new[] { doc, control })
        {
            ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", "blue");
            ((HTMLElement)document.GetElementById(id)!).Style.SetProperty(property, value);
        }
        reference.Invalidate(); Compare(doc, styles, control, reference);
        foreach (var document in new[] { doc, control })
            ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", "green");
        reference.Invalidate(); Compare(doc, styles, control, reference);
    }
    [TestCase("#c{width:10cqw}", "width", "300px")]
    [TestCase("#c{width:10cqi}", "writing-mode", "vertical-rl")]
    [TestCase("#c{width:2rem}", "font-size", "24px")]
    public void ContainerAndFontBasesStayLiveAcrossRepeatedInlineRefreshes(string css, string property, string value)
    {
        var (doc, styles) = Load(true, css); var (control, reference) = Load(false, css);
        Compare(doc, styles, control, reference);
        foreach (var document in new[] { doc, control })
        {
            ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", "blue");
            ((HTMLElement)document.GetElementById("p")!).Style.SetProperty(property, value);
        }
        reference.Invalidate(); Compare(doc, styles, control, reference);
        foreach (var document in new[] { doc, control })
            ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", "green");
        reference.Invalidate(); Compare(doc, styles, control, reference);
    }
    [Test]
    public void RootFontBasisChangesEvenWhenTheLocalFontSizeStaysAbsolute()
    {
        var (doc, styles) = Load(true, "#c{font-size:10px;width:2rem}");
        var (control, reference) = Load(false, "#c{font-size:10px;width:2rem}");
        Compare(doc, styles, control, reference);
        foreach (var document in new[] { doc, control })
            ((HTMLElement)document.DocumentElement!).Style.SetProperty("font-size", "24px");
        reference.Invalidate(); Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedStyle(doc.GetElementById("c")!).GetPropertyValue("width"), Is.EqualTo("48px"));
    }
    [Test]
    public void TypedScalarWritesRemainLiveDuringAConcurrentPaletteRefresh()
    {
        var (doc, styles) = Load(true); var (control, reference) = Load(false);
        var left = ((HTMLElement)doc.GetElementById("c")!).AttributeStyleMap;
        var right = ((HTMLElement)control.GetElementById("c")!).AttributeStyleMap;
        left.Set("width", new CSSUnitValue(10, "px")); right.Set("width", new CSSUnitValue(10, "px"));
        Compare(doc, styles, control, reference);
        // The public map snapshots the old value before replacing its declaration-owned slot's Current.
        left.Set("width", new CSSUnitValue(45, "px")); right.Set("width", new CSSUnitValue(45, "px"));
        foreach (var document in new[] { doc, control })
            ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", "blue");
        reference.Invalidate(); Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedStyle(doc.GetElementById("c")!).GetPropertyValue("width"), Is.EqualTo("45px"));
    }
    [Test]
    public void SuppliedContextsAndMediaChangesCannotReuseAStaleComputedBasis()
    {
        var (doc, styles) = Load(true, "#c{width:2em;height:10vw}"); var (control, reference) = Load(false, "#c{width:2em;height:10vw}");
        double size = 12;
        styles.ValueContext = _ => new ComputedValueContext { Font = new ComputedFontMetrics(Size: size) };
        reference.ValueContext = styles.ValueContext;
        Compare(doc, styles, control, reference);
        size = 30;
        styles.Media = new MediaEnvironment { Width = 400, Height = 300 };
        reference.Media = styles.Media;
        foreach (var document in new[] { doc, control })
            ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", "blue");
        Compare(doc, styles, control, reference);
        Assert.That(styles.ReusedComputedValueCount, Is.Zero);
    }
    // Allocation-only change; CSS Sizing 3 box-sizing and min/max constraints.
    [TestCase(false, true, false, 100)]
    [TestCase(false, true, true, 120)]
    [TestCase(false, false, false, 80)]
    [TestCase(false, false, true, 96)]
    [TestCase(true, true, false, 80)]
    [TestCase(true, true, true, 100)]
    [TestCase(true, false, false, 64)]
    [TestCase(true, false, true, 80)]
    public void WarmBoxBasisReadsAllocateNoPropertyNameStrings(bool specifiedBorderBox, bool width, bool borderBox, double expected)
    {
        var (doc, styles) = Load(true, BoxBasisCss + (specifiedBorderBox ? "#c{box-sizing:border-box}" : ""));
        Func<double?> read = BindBoxBasis(styles, doc.GetElementById("c")!, width, borderBox);
        bool correct = true;
        long allocated = AllocationWindow.Measure(1024, _ => correct &= read() == expected);
        TestContext.WriteLine($"Box basis {specifiedBorderBox}/{width}/{borderBox}: {allocated} bytes / 1024 calls");
        Assert.That(correct, Is.True, "Every actual helper result, including all warmup calls, must retain the CSS box.");
        Assert.That(allocated, Is.Zero);
    }

    [TestCase("width:auto", null)]
    [TestCase("width:50%", null)]
    [TestCase("min-width:20%", null)]
    [TestCase("max-width:50%", null)]
    [TestCase("padding-left:10%", null)]
    [TestCase("min-width:120px;max-width:80px", 120d)]
    [TestCase("min-width:0px;max-width:50px", 50d)]
    [TestCase("width:10px;box-sizing:border-box", 0d)]
    public void BoxBasisKeepsConstraintsAndUnresolvedInputs(string declarations, double? expected)
    {
        var (doc, styles) = Load(true, BoxBasisCss + "#c{" + declarations + "}");
        Assert.That(BindBoxBasis(styles, doc.GetElementById("c")!, true, false)(), Is.EqualTo(expected));
    }

    [Test]
    public void BoxBasisKeepsLiveEdgeChangesAndContainerAndOriginConsumers()
    {
        const string css = BoxBasisCss + "#c{container-type:size;transform-origin:50% 50%;perspective-origin:50% 50%}" +
            "#g{width:10cqw;height:10cqh}";
        var (doc, styles) = Load(true, css); var (control, reference) = Load(false, css);
        Compare(doc, styles, control, reference);
        var child = (HTMLElement)doc.GetElementById("c")!;
        var read = BindBoxBasis(styles, child, true, true);
        Assert.That(read(), Is.EqualTo(120));
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("transform-origin"), Is.EqualTo("60px 48px"));
        Assert.That(styles.GetComputedStyle(doc.GetElementById("g")!).GetPropertyValue("width"), Is.EqualTo("10px"));
        foreach (var document in new[] { doc, control })
            ((HTMLElement)document.GetElementById("c")!).Style.SetProperty("border-style", "hidden");
        reference.Invalidate(); Compare(doc, styles, control, reference);
        Assert.That(read(), Is.EqualTo(106));
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("perspective-origin"), Is.EqualTo("53px 42px"));
    }

    private const string BoxBasisCss = "#c{display:block;width:100px;height:80px;padding:1px 2px 3px 4px;" +
        "border-style:solid;border-width:5px 6px 7px 8px;min-width:0px;min-height:0px;max-width:none;max-height:none}";
    private static Func<double?> BindBoxBasis(StyleEngine styles, Element element, bool width, bool borderBox)
    {
        // Bind the real private helper once, outside every AllocationWindow. No reflection, boxing,
        // argument arrays, replacement algorithm or production instrumentation occurs in its 1024 calls.
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var method = typeof(StyleEngine).GetMethod("DefiniteBoxAxis", flags)!;
        var entry = typeof(StyleEngine).GetMethod("GetEntry", flags)!.Invoke(styles, new object[] { element });
        var call = System.Linq.Expressions.Expression.Call(method,
            System.Linq.Expressions.Expression.Constant(entry, method.GetParameters()[0].ParameterType),
            System.Linq.Expressions.Expression.Constant(width), System.Linq.Expressions.Expression.Constant(borderBox));
        return System.Linq.Expressions.Expression.Lambda<Func<double?>>(call).Compile();
    }
    [Test]
    public void FixedReuseSlotsRejectMismatchedOrdinalsNamesAndSpecifiedIdentity()
    {
        var (doc, styles) = Load(true); var (control, reference) = Load(false);
        Compare(doc, styles, control, reference);
        var child = doc.GetElementById("c")!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object entry = typeof(StyleEngine).GetMethod("GetEntry", flags)!.Invoke(styles, new object[] { child })!;
        object cache = entry.GetType().GetField("ComputedReuse", flags)!.GetValue(entry)!;
        var field = cache.GetType().GetField("_values", flags)!;
        Assert.That(field.GetValue(cache), Is.InstanceOf<Array>(), "One catalog-sized array replaces the per-entry name dictionary.");
        var slots = (Array)field.GetValue(cache)!;
        int ordinal = -1;
        for (int i = 0; i < slots.Length; i++)
            if ((string?)slots.GetValue(i)!.GetType().GetProperty("Name")!.GetValue(slots.GetValue(i)) == "opacity")
                ordinal = i;
        Assert.That(ordinal, Is.GreaterThanOrEqualTo(0), "Exercise a genuinely captured immutable scalar.");
        var specified = (System.Collections.Generic.Dictionary<string, CssValue>)entry.GetType().GetField("Specified", flags)!.GetValue(entry)!;
        var read = cache.GetType().GetMethod("TryValue", flags)!;
        Assert.That(read.Invoke(cache, new object?[] { entry, null, ordinal, "opacity", specified["opacity"], null }), Is.True);
        Assert.That(read.Invoke(cache, new object?[] { entry, null, ordinal, "width", specified["opacity"], null }), Is.False,
            "Matching specified identity at the wrong name must not return an opacity computation as width.");
        Assert.That(read.Invoke(cache, new object?[] { entry, null, (ordinal + 1) % slots.Length, "opacity", specified["opacity"], null }), Is.False,
            "Reordered iteration must miss and recompute, not reuse another property's slot.");
        Assert.That(read.Invoke(cache, new object?[] { entry, null, ordinal, "opacity", new CssNumberValue(1, false), null }), Is.False,
            "Equal-looking but different specified values still require the existing identity admission.");
        for (int i = 0; i < 4; i++)
        {
            foreach (var document in new[] { doc, control })
                ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", $"rgb({i} 11 13)");
            reference.Invalidate(); Compare(doc, styles, control, reference);
            Assert.That(field.GetValue(cache), Is.SameAs(slots), "Warm refreshes do not replace the slot array.");
        }
    }

    [TestCase("Values")]
    [TestCase("Specified")]
    public void ScalarReuseKeepsOneMapAcrossPaletteAndGeometryRefreshes(string map)
    {
        var (doc, styles) = Load(true); var (control, reference) = Load(false);
        Compare(doc, styles, control, reference);
        var child = doc.GetElementById("c")!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object entry = typeof(StyleEngine).GetMethod("GetEntry", flags)!.Invoke(styles, new object[] { child })!;
        var field = entry.GetType().GetField(map, flags)!;
        object original = field.GetValue(entry)!;
        long reused = styles.ReusedComputedValueCount;
        bool retained = true;
        for (int i = 0; i < 20; i++)
        {
            foreach (var document in new[] { doc, control })
            {
                ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", $"rgb({i} 11 13)");
                ((HTMLElement)document.GetElementById("c")!).Style.SetProperty("clip-path", $"polygon(0px 0px,{20 + i}px 0px,20px 20px)");
            }
            reference.Invalidate(); Compare(doc, styles, control, reference);
            retained &= ReferenceEquals(field.GetValue(entry), original);
        }
        TestContext.WriteLine($"Single {map} map across 20 real refreshes: " + retained);
        Assert.That(styles.ReusedComputedValueCount, Is.GreaterThan(reused));
        Assert.That(retained, Is.True, "Stable refreshes must retain entry-owned maps, not copy a second map.");
    }

    [Test]
    public void SpecifiedRetentionReselectsRemovedDeclarationsAfterDirectionAndProviderFallbacks()
    {
        const string css = "#c{margin-inline-start:inherit}.changed{color:purple}";
        var (doc, styles) = Load(true, css); var (control, reference) = Load(false, css);
        Compare(doc, styles, control, reference);
        for (int i = 0; i < 4; i++)
        {
            foreach (var document in new[] { doc, control })
            {
                var child = (HTMLElement)document.GetElementById("c")!;
                var parent = (HTMLElement)document.GetElementById("p")!;
                child.Style.SetProperty("width", "17px");
                child.Style.SetProperty("padding-inline-start", "inherit");
                parent.Style.SetProperty("direction", i % 2 == 0 ? "rtl" : "ltr");
            }
            reference.Invalidate(); Compare(doc, styles, control, reference);
            styles.ValueContext = _ => default; reference.ValueContext = styles.ValueContext;
            foreach (var document in new[] { doc, control })
            {
                var child = (HTMLElement)document.GetElementById("c")!;
                child.Style.RemoveProperty("width"); child.Style.RemoveProperty("padding-inline-start");
                child.ClassName = i % 2 == 0 ? "changed" : "";
            }
            reference.Invalidate(); Compare(doc, styles, control, reference);
            styles.ValueContext = null; reference.ValueContext = null;
            ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("--paint", i % 2 == 0 ? "blue" : "red");
            ((HTMLElement)control.GetElementById("p")!).Style.SetProperty("--paint", i % 2 == 0 ? "blue" : "red");
            reference.Invalidate(); Compare(doc, styles, control, reference);
        }
    }

    [Test]
    public void FailedRefreshCannotAdmitPartiallyUpdatedScalarReuseRecords()
    {
        var (doc, styles) = Load(true); var (control, reference) = Load(false);
        Compare(doc, styles, control, reference);
        var child = (HTMLElement)doc.GetElementById("c")!;
        var failing = new FailingNumber();
        child.Style.SetTypedProperty("opacity", failing);
        var error = Assert.Throws<InvalidOperationException>(() => styles.GetComputedStyle(child));
        Assert.That(error!.Message, Is.EqualTo("typed numeric fixture failed"));
        long reused = styles.ReusedComputedValueCount;
        failing.Fail = false; // CSSOM compares the old numeric value when replacing it.
        child.Style.SetProperty("opacity", "0.75");
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("opacity"), Is.EqualTo("0.75"));
        Assert.That(styles.ReusedComputedValueCount, Is.EqualTo(reused),
            "A failed snapshot must be fully recomputed, not re-admitted from a partial record set.");
        ((HTMLElement)control.GetElementById("c")!).Style.SetProperty("opacity", "0.75");
        reference.Invalidate(); Compare(doc, styles, control, reference);
    }

    private sealed class FailingNumber() : CssNumberValue(0, false)
    {
        internal bool Fail = true;
        public override double Value => Fail ? throw new InvalidOperationException("typed numeric fixture failed") : .5;
    }
}
