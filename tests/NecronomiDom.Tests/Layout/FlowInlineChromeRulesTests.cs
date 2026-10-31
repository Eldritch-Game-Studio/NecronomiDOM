using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class FlowInlineChromeRulesTests
{
    // Deliberately synthetic advances isolate layout from shaping.
    private sealed class Metrics(double ascent = 8, double descent = 2) : IInlineTextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
            => new(InlineAdvance(text, text.Length, fontSize, fontWeight, letterSpacing, italic), ascent, descent);
        public double InlineAdvance(string text, int offset, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
        {
            double advance = 0;
            for (int i = 0; i < text.Length && i < offset; i++)
            {
                if (text[i] == 'f' && i + 1 < text.Length && text[i + 1] == 'i') { advance += 9.05 + letterSpacing; i++; }
                else advance += 6.1 + letterSpacing;
            }
            return advance;
        }
        public Rect InlineInkBounds(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
            => new(0, -ascent, Measure(text, fontSize, fontWeight, letterSpacing, italic).Width, ascent + descent);
    }
    private static DisplayList Fixture(string markup, Metrics metrics, string css = "")
    {
        var document = HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;border:0 solid black;appearance:none;font-family:test;font-size:10px;line-height:14px;color:black}html,body{width:100%;height:100%}#line{width:500px}" + css + "</style><div id=line>" + markup + "</div>", "inline-rules", WebUrl.Parse("res:///inline-rules.html")!).Document;
        return new FlowLayout(new StyleEngine(document), metrics, "test").Layout(document, 800, 720);
    }
    private static TextRun[] Texts(DisplayList display) => display.Runs.OfType<TextRun>().ToArray();
    [Test]
    public void NodeBoundariesKeepTheWholeLigatureInItsFirstNodeAndSnapEachItemWidth()
    {
        var display = Fixture("Con<span id=owner>f</span><span id=tail>i</span>rm", new Metrics());
        var runs = Texts(display);
        Assert.That(runs.All(r => r.ShapingText == "Confirm"), Is.True, "a DOM boundary must not reshape the fi substring");
        Assert.That(runs.Select(r => r.Text), Is.EqualTo(new[] { "Con", "f", "i", "rm" }));
        Assert.That(runs.Select(r => r.InlineFragment!.Bounds.X), Is.EqualTo(new[] { 0d, 18.3125, 27.375, 27.375 }));
        Assert.That(runs.Select(r => r.InlineFragment!.Bounds.Width), Is.EqualTo(new[] { 18.3125, 9.0625, 0d, 12.203125 }));
        Assert.That(runs.Select(r => r.InlineFragment!.SourceOffsets.Count), Is.EqualTo(new[] { 4, 2, 2, 3 }));
    }
    [Test]
    public void SnappedItemWidthsDriveWrappingAndAlignmentRatherThanOnlyPaintBounds()
    {
        var wrapped = Fixture("<span>A</span> B", new Metrics(), "#line{width:18.31px}");
        Assert.That(Texts(wrapped).Select(r => r.Text), Is.EqualTo(new[] { "A", "B" }), "three 6.1px advances become two independently snapped DOM items (>18.31px)");
        var centered = Fixture("<span>A</span>B", new Metrics(), "#line{width:20px;text-align:center}");
        Assert.That(Texts(centered).First().InlineFragment!.Bounds.X, Is.EqualTo((20 - 12.21875) / 2));
    }
    [Test]
    public void FlatTextRetainsItsExistingHalfLeadingBaseline()
    {
        var flat = Fixture("M", new Metrics(38, 11), "#line{font-size:36px;line-height:60px}");
        Assert.That(Texts(flat).Single().Baseline, Is.EqualTo(43.5));
        Assert.That(Texts(flat).Single().InlineFragment, Is.Null);
    }
}
