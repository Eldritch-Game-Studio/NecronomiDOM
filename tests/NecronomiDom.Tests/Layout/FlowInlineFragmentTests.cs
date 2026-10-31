using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class FlowInlineFragmentTests
{
    private sealed class Metrics : IInlineTextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
            => new(text.Length * fontSize + text.Length * letterSpacing - (text.Contains("AV", StringComparison.Ordinal) ? 4 : 0), fontSize * .8, fontSize * .2);
        public double InlineAdvance(string text, int offset, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
            => offset * (fontSize + letterSpacing) - (text.IndexOf("AV", StringComparison.Ordinal) is int pair && pair >= 0 && offset > pair ? 4 : 0);
        public Rect InlineInkBounds(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
            => new(0, -fontSize * .8, Measure(text, fontSize, fontWeight, letterSpacing, italic).Width, fontSize);
    }
    private static (Document Document, FlowLayout Flow) Fixture(string markup, string css = "")
    {
        var document = HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;border:0 solid black;appearance:none;font-family:test;font-size:10px;line-height:14px;color:black}html,body{width:100%;height:100%}#line{width:70px}" + css + "</style><div id=line>" + markup + "</div>", "inline", WebUrl.Parse("res:///inline.html")!).Document;
        return (document, new FlowLayout(new StyleEngine(document), new Metrics(), "test"));
    }
    private static TextRun[] Texts(DisplayList display) => display.Runs.Select(r => r is TextEffectsRun effect ? effect.Text : r).OfType<TextRun>().ToArray();
    [Test]
    public void VoiceWordColorAndBackgroundWrapInOneContinuousFlow()
    {
        var f = Fixture("AA <span id=word>BB CC DD</span> EE", "#word{color:#d6aa58;background-color:blue}");
        var display = f.Flow.Layout(f.Document, 400, 300);
        var runs = Texts(display);
        Assert.That(runs.Select(r => r.Text), Is.EqualTo(new[] { "AA ", "BB", "CC DD", "EE" }));
        Assert.That(runs.Select(r => r.ShapingText), Is.EqualTo(new[] { "AA BB", "AA BB", "CC DD", "EE" }));
        Assert.That(display.Boxes.Single(b => b.Element.Id == "line").Bounds.Height, Is.EqualTo(42));
        var word = display.Boxes.Where(b => b.Element.Id == "word").ToArray();
        Assert.That(word.Select(b => b.Bounds), Is.EqualTo(new[] { new Rect(30, 2, 20, 10), new Rect(0, 16, 50, 10) }));
        Assert.That(display.HitTest(35, 4)?.Id, Is.EqualTo("word"));
        Assert.That(display.HitTest(10, 18)?.Id, Is.EqualTo("word"));
        Assert.That(display.Runs.OfType<BoxRun>().Count(b => b.Style.Background == new Rgba(0, 0, 1, 1)), Is.EqualTo(2));
        Assert.That(runs.Where(r => r.InlineFragment!.Node.ParentElement!.Id == "word").All(r => r.Color == new Rgba(214f / 255, 170f / 255, 88f / 255, 1)), Is.True);
    }
    [Test]
    public void MidWordPaintBoundaryKeepsKerningContextAndRangeGeometry()
    {
        var f = Fixture("A<span id=word>V</span>XY", "#word{color:red}");
        var display = f.Flow.Layout(f.Document, 400, 300); var runs = Texts(display);
        Assert.That(runs.Select(r => r.Text), Is.EqualTo(new[] { "A", "V", "XY" }));
        Assert.That(runs.All(r => r.ShapingText == "AVXY" && r.X == 0), Is.True);
        Assert.That(runs.Select(r => r.InlineFragment!.Bounds.X), Is.EqualTo(new[] { 0d, 6d, 16d }));
        Assert.That(runs.Select(r => r.InlineFragment!.Start), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(runs.Sum(r => r.InlineFragment!.Bounds.Width), Is.EqualTo(36));
    }
    [Test]
    public void WrappedInlineFragmentsRemainOneSequentialFocusTarget()
    {
        var f = Fixture("<span id=word tabindex=0>AA BB CC DD</span><span id=after tabindex=0>EE</span>");
        var display = f.Flow.Layout(f.Document, 400, 300);
        using var input = new DocumentInput(f.Document, new StyleEngine(f.Document));
        input.UpdateGeometry(display, 400, 300);
        Assert.That(input.MoveSequential(false), Is.True);
        Assert.That(f.Document.FocusedElement?.Id, Is.EqualTo("word"));
        Assert.That(input.MoveSequential(false), Is.True);
        Assert.That(f.Document.FocusedElement?.Id, Is.EqualTo("after"), "a wrapped span is not re-entered for each fragment");
        Assert.That(input.MoveSequential(true), Is.True);
        Assert.That(f.Document.FocusedElement?.Id, Is.EqualTo("word"));
    }
    [Test]
    public void WhitespaceCollapsesAcrossNodesWithoutLosingDomSelectionOffsets()
    {
        var f = Fixture("  AA <span id=word>  BB </span>  CC  ", "#line{width:200px}");
        var runs = Texts(f.Flow.Layout(f.Document, 400, 300));
        Assert.That(string.Concat(runs.Select(r => r.Text)), Is.EqualTo("AA BB CC"));
        var word = runs.Single(r => r.InlineFragment!.Node.ParentElement!.Id == "word").InlineFragment!;
        Assert.That(word.SourceOffsets, Is.EqualTo(new[] { 2, 3, 4, 5 }));
        Assert.That(word.Node.Data, Is.EqualTo("  BB "));
    }
    [Test]
    public void WeightAndEffectsUseFragmentStylesButColorOnlyBoundariesShareShaping()
    {
        var f = Fixture("A<span id=word>BB</span>C", "#word{font-weight:700;letter-spacing:1px;-webkit-text-stroke:1px red;text-shadow:1px 1px blue}");
        var display = f.Flow.Layout(f.Document, 400, 300);
        var effect = display.Runs.OfType<TextEffectsRun>().Single();
        Assert.That((effect.Text.Text, effect.Text.FontWeight, effect.Text.LetterSpacing), Is.EqualTo(("BB", 700d, 1d)));
        Assert.That(effect.Text.ShapingText, Is.EqualTo("BB"));
        Assert.That(effect.Text.InlineFragment!.Start, Is.Zero, "font changes start a new shape, not a new DOM text stream");
        Assert.That(effect.Effects.StrokeWidth, Is.EqualTo(1));
        Assert.That(effect.Effects.Shadows.Length, Is.EqualTo(1));
        Assert.That(Texts(display).Select(r => r.X), Is.EqualTo(new[] { 0d, 10d, 32d }));
    }
    [Test]
    public void NestedInlineBackgroundsAndHitOwnershipFollowDomPaintOrder()
    {
        var f = Fixture("A<span id=outer>B<span id=inner>CD</span>E</span>F", "#outer{background:yellow}#inner{background:lime}");
        var display = f.Flow.Layout(f.Document, 400, 300);
        Assert.That(display.Boxes.Where(b => b.Element.Id is "outer" or "inner").Select(b => (b.Element.Id, b.Bounds.Width)), Is.EqualTo(new[] { ("outer", 40d), ("inner", 20d) }));
        Assert.That(display.HitTest(25, 5)?.Id, Is.EqualTo("inner"));
        Assert.That(display.HitTest(45, 5)?.Id, Is.EqualTo("outer"));
    }
    [Test]
    public void LiveWordUpdatesChangeOnlyFragmentPaintNotLineGeometry()
    {
        var f = Fixture("AA <span id=word>BB</span> CC", "#word{color:red}");
        var first = f.Flow.Layout(f.Document, 400, 300);
        ((HTMLElement)f.Document.GetElementById("word")!).Style.SetProperty("color", "blue");
        var second = f.Flow.Layout(f.Document, 400, 300);
        Assert.That(second.Boxes.Select(b => b.Bounds), Is.EqualTo(first.Boxes.Select(b => b.Bounds)));
        Assert.That(Texts(second).Select(r => r.InlineFragment!.Bounds), Is.EqualTo(Texts(first).Select(r => r.InlineFragment!.Bounds)));
        Assert.That(Texts(second).Single(r => r.Text == "BB").Color, Is.EqualTo(new Rgba(0, 0, 1, 1)));
    }
    [Test]
    public void InlineFlowContributesItsActualHeightAndIntrinsicWidthToFlexSizing()
    {
        var f = Fixture("AA <span>BB CC</span>", "body{display:flex;align-items:flex-start}#line{width:70px;flex-shrink:0}");
        var display = f.Flow.Layout(f.Document, 400, 300);
        Assert.That(display.Boxes.Single(b => b.Element.Id == "line").Bounds.Height, Is.EqualTo(28));
    }
    [Test]
    public void FlatTextStillUsesTheByteIdenticalOrdinaryRunRepresentation()
    {
        var f = Fixture("AA BB CC"); var runs = Texts(f.Flow.Layout(f.Document, 400, 300));
        Assert.That(runs, Is.EqualTo(new[] { new TextRun("AA BB", 0, 10, 10, new Rgba(0, 0, 0, 1)) { FontFamily = "test" }, new TextRun("CC", 0, 24, 10, new Rgba(0, 0, 0, 1)) { FontFamily = "test" } }));
    }
    [TestCase("A<span style='padding:1px'>B</span>")]
    [TestCase("A<span>B</span><div>C</div>")]
    public void UnimplementedInlineBoxFeaturesFailRatherThanSilentlyFlatten(string markup)
    {
        var f = Fixture(markup);
        Assert.Throws<NotSupportedException>(() => f.Flow.Layout(f.Document, 400, 300));
    }
}
