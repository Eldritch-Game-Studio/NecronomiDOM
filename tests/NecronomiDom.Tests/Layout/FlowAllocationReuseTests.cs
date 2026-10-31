using System;
using System.Linq;
using System.Text;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Values;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class FlowAllocationReuseTests
{
    private static CssValue[] Values() =>
    [
        new CssKeywordValue("initial"), new CssStringValue("quoted text"), new CssDelimValue("/"),
        new CssNumberValue(1.25, false), new CssIntegerValue(2), new CssDimensionValue(12, "px"),
        new CssPercentageValue(50), new CssFunctionValue("f", new CssListValue(
            [new CssDimensionValue(1, "px"), new CssPercentageValue(25)], CssListSeparator.Space)),
        new CssBlockValue(new CssKeywordValue("line"))
    ];

    [Test]
    [NonParallelizable]
    public void InitialRepresentationProofMatchesSerializationWithoutWarmAllocation()
    {
        // Preserve output and pool update buffers.
        var first = Values(); var second = Values();
        for (int i = 0; i < first.Length; i++)
        {
            Assert.That(FlowLayout.ProfileValuesSerializeIdentically(first[i], second[i]), Is.True);
            Assert.That(first[i].Serialize(), Is.EqualTo(second[i].Serialize()));
        }
        var (allocated, equal) = MeasureInitialRepresentationProof(first, second, serialize: false);
        Assert.That(equal, Is.True);
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    [NonParallelizable]
    public void InitialRepresentationAllocationWindowStillDetectsSerializationBuffers()
    {
        // Real serialization is the allocating alternative this representation check avoids.
        // The same preparation and counter helper must not hide its allocations.
        var (allocated, equal) = MeasureInitialRepresentationProof(Values(), Values(), serialize: true);
        Assert.That(equal, Is.True);
        Assert.That(allocated, Is.GreaterThan(0));
    }

    private const int InitialRepresentationSampleCount = 1000;
    private static (long Allocated, bool Equal) MeasureInitialRepresentationProof(
        CssValue[] first, CssValue[] second, bool serialize)
    {
        // Every one of the 1000 × 9 comparisons
        // stays inside the counters, and equality must hold in every warm-up and measured window.
        bool equal = true;
        long allocated = AllocationWindow.Measure(InitialRepresentationSampleCount, _ =>
        {
            for (int i = 0; i < first.Length; i++)
                equal &= serialize
                    ? first[i].Serialize() == second[i].Serialize()
                    : FlowLayout.ProfileValuesSerializeIdentically(first[i], second[i]);
        });
        return (allocated, equal);
    }

    [Test]
    public void InitialRepresentationProofRereadsMutableListsAndRejectsUnknownSerializers()
    {
        CssValue[] children = [new CssDimensionValue(1, "px")];
        var actual = new CssFunctionValue("f", new CssListValue(children, CssListSeparator.Space));
        var initial = new CssFunctionValue("f", new CssListValue([new CssDimensionValue(1, "px")], CssListSeparator.Space));
        Assert.That(FlowLayout.ProfileValuesSerializeIdentically(actual, initial), Is.True);
        children[0] = new CssDimensionValue(2, "px");
        Assert.That(FlowLayout.ProfileValuesSerializeIdentically(actual, initial), Is.False);
        Assert.That(actual.Serialize(), Is.Not.EqualTo(initial.Serialize()));
        Assert.That(FlowLayout.ProfileValuesSerializeIdentically(new AlternateDimension("first"), new AlternateDimension("second")), Is.False);
        Assert.That(FlowLayout.ProfileValuesSerializeIdentically(new CssNumberValue(0, false), new CssNumberValue(-0d, false)), Is.False);
        Assert.That(FlowLayout.ProfileValuesSerializeIdentically(new CssListValue(children, CssListSeparator.Space),
            new CssListValue(children, CssListSeparator.Comma)), Is.False);
    }

    private sealed class AlternateDimension(string text) : CssDimensionValue(1, "px")
    { public override void AppendTo(StringBuilder builder) => builder.Append(text); }
    private sealed class GlyphMetrics : ITextShaper
    {
        internal bool Throw;
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
        {
            if (Throw && text == "THROW") throw new InvalidOperationException("requested shaper failure");
            return new(text.Length * 8, 12, 4);
        }
    }

    [Test]
    public void RecursiveFlexBuffersReturnAfterExceptionsAndDoNotAccumulateChildrenOrText()
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div,p{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;font-family:monospace;font-size:16px;line-height:20px;appearance:none}" +
            "html,body{width:100%;height:100%;background-color:white}" +
            "#outer{position:relative;display:flex;width:300px;height:100px;align-items:flex-start}" +
            "#inner{display:flex;align-items:flex-start}#absolute{position:absolute;width:10px;height:10px}" +
            "</style><div id=outer><div id=inner>lead<p id=label>AA</p>tail</div><div id=absolute></div></div>",
            "flex-buffer", WebUrl.Parse("res:///flex-buffer.html")!).Document;
        var metrics = new GlyphMetrics();
        var flow = new FlowLayout(new StyleEngine(doc), metrics, "monospace");
        var first = flow.Layout(doc, 400, 300);
        flow.Layout(doc, 400, 300);
        int buffers = flow.FlexCollectionBufferCount;
        Assert.That(buffers, Is.GreaterThan(1), "recursive flex calls must own separate collection buffers");
        doc.GetElementById("label")!.TextContent = "THROW";
        metrics.Throw = true;
        Assert.Throws<InvalidOperationException>(() => flow.Layout(doc, 400, 300));
        metrics.Throw = false;
        doc.GetElementById("label")!.TextContent = "AA";
        for (int repeat = 0; repeat < 3; repeat++)
        {
            var next = flow.Layout(doc, 400, 300);
            Assert.That(next.Boxes.Select(box => box.Bounds).ToArray(), Is.EqualTo(first.Boxes.Select(box => box.Bounds).ToArray()));
            Assert.That(flow.FlexCollectionBufferCount, Is.EqualTo(buffers));
        }
    }

    [Test]
    public void UntransformedHitMetadataCanReuseImmutableBoxButChangedLinksAndCoordinateBitsCannot()
    {
        var doc = HtmlImporter.Import("<div id=box></div>", "hit-buffer", WebUrl.Parse("res:///hit-buffer.html")!).Document;
        var element = doc.GetElementById("box")!;
        var bounds = new Rect(0, 0, 20, 30);
        var box = new LayoutBox(element, bounds, true);
        Assert.That(FlowLayout.SameHitGeometry(box, null, null, bounds), Is.True);
        Assert.That(FlowLayout.SameHitGeometry(box, null, null, bounds with { X = -0d }), Is.False);
        var clip = new LayoutHitClip(null, element, bounds, null, 0, false);
        Assert.That(FlowLayout.SameHitGeometry(box, clip, null, bounds), Is.False);
        var transform = LayoutHitTransform.From(new Affine2D(1, 0, 0, 1, 2, 3));
        var transformedBounds = new Rect(2, 3, 20, 30);
        var transformed = box with { HitClip = clip, HitTransform = transform, VisualBounds = transformedBounds };
        Assert.That(FlowLayout.SameHitGeometry(transformed, clip, transform, transformedBounds), Is.False,
            "cached inverse transforms conservatively keep the original record rebuild path");
        Assert.That(FlowLayout.SameHitGeometry(transformed, new LayoutHitClip(null, element, bounds, null, 0, false), transform, transformedBounds), Is.False);
        Assert.That(FlowLayout.SameHitGeometry(transformed, clip, LayoutHitTransform.From(new Affine2D(1, 0, 0, 1, 2, 3)), transformedBounds), Is.False);
        Assert.That(FlowLayout.SameHitGeometry(transformed, clip, transform, bounds), Is.False);
        Assert.That(box.VisualBounds, Is.EqualTo(bounds), "the previous published box remains immutable");
    }
}
