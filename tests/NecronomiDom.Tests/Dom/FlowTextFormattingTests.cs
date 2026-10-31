using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class FlowTextFormattingTests
{
    private sealed class FontMetrics(double lineGap = 0) : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text == "\u2026" ? 8 : text.Length * 6, 11, 4) { LineGap = lineGap };
    }
    private static DisplayList Fixture(string text, string css = "", bool intrinsic = false, double lineGap = 0)
    {
        var document = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;font-family:monospace;font-size:10px;line-height:normal;appearance:none}" +
            "html,body{width:100%;height:100%;background-color:white}#line{width:100px}" + css +
            "</style>" + (intrinsic ? "<div id=host><div id=line>" + text + "</div></div>" : "<div id=line>" + text + "</div>"),
            "text-formatting", WebUrl.Parse("res:///text-formatting.html")!).Document;
        return new FlowLayout(new StyleEngine(document), new FontMetrics(lineGap), "monospace").Layout(document, 400, 300);
    }
    private static Rect LineBounds(DisplayList display) => display.Boxes.Single(box => box.Element.Id == "line").Bounds;

    [Test]
    public void NormalLineHeightUsesTheActualFontStrutRatherThanFontSizeMultiplier()
    {
        var display = Fixture("0 / 0!");
        Assert.That(LineBounds(display).Height, Is.EqualTo(15));
        var text = display.Runs.OfType<TextRun>().Single();
        Assert.That(text.Text, Is.EqualTo("0 / 0!"));
        Assert.That(text.Baseline, Is.EqualTo(11));
    }
    [Test]
    public void NormalStrutIncludesFontLineGapWhileExplicitLineHeightKeepsItsAuthoredValue()
    {
        var normal = Fixture("A", lineGap: 2);
        Assert.That(LineBounds(normal).Height, Is.EqualTo(17));
        Assert.That(normal.Runs.OfType<TextRun>().Single().Baseline, Is.EqualTo(12));
        var explicitHeight = Fixture("A", "#line{line-height:20px}", lineGap: 2);
        Assert.That(LineBounds(explicitHeight).Height, Is.EqualTo(20));
        Assert.That(explicitHeight.Runs.OfType<TextRun>().Single().Baseline, Is.EqualTo(13.5));
    }
    [Test]
    public void NoWrapRetainsOneLineWhileNormalWhitespaceWrapsTheSameWords()
    {
        var normal = Fixture("AA BB CC", "#line{width:18px}");
        var nowrap = Fixture("AA BB CC", "#line{width:18px;white-space:nowrap}");
        Assert.That(LineBounds(normal).Height, Is.EqualTo(45));
        Assert.That(normal.Runs.OfType<TextRun>().Select(run => run.Text), Is.EqualTo(new[] { "AA", "BB", "CC" }));
        Assert.That(LineBounds(nowrap).Height, Is.EqualTo(15));
        Assert.That(nowrap.Runs.OfType<TextRun>().Single().Text, Is.EqualTo("AA BB CC"));
    }
    [Test]
    public void EllipsisIsAdjacentToRemainingContentNotPaddedToTheContainerEdge()
    {
        var display = Fixture("ABCDEFG", "#line{width:23px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}");
        var text = display.Runs.OfType<TextRun>().ToArray();
        Assert.That(text.Select(run => run.Text), Is.EqualTo(new[] { "AB", "\u2026" }));
        Assert.That(text.Select(run => run.X), Is.EqualTo(new[] { 0d, 12d }));
        Assert.That(text.Select(run => run.Baseline), Is.EqualTo(new[] { 11d, 11d }));
        Assert.That(display.Runs.OfType<BeginRectClipRun>().Any(run => run.Bounds.Width == 23), Is.True);
    }
    [Test]
    public void FirstCharacterIsClippedRatherThanReplacedWhenAnEllipsisCannotFit()
    {
        var display = Fixture("ABCDEFG", "#line{width:4px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}");
        var text = display.Runs.OfType<TextRun>().ToArray();
        Assert.That(text.Select(run => run.Text), Is.EqualTo(new[] { "A", "\u2026" }));
        Assert.That(text[1].X, Is.EqualTo(6));
        Assert.That(display.Runs.OfType<BeginRectClipRun>().Any(run => run.Bounds.Width == 4), Is.True);
    }
    [Test]
    public void CenteredOverflowUsesTheBrowserStartFallbackBeforeElision()
    {
        var display = Fixture("ABCDEFG", "#line{width:23px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;text-align:center}");
        var text = display.Runs.OfType<TextRun>().ToArray();
        Assert.That(text.Select(run => run.Text), Is.EqualTo(new[] { "AB", "\u2026" }));
        Assert.That(text.Select(run => run.X), Is.EqualTo(new[] { 0d, 12d }));
    }
    [Test]
    public void EndAlignedOverflowUsesTheBrowserStartFallbackBeforeElision()
    {
        var display = Fixture("ABCDEFG", "#line{width:23px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;text-align:right}");
        var text = display.Runs.OfType<TextRun>().ToArray();
        Assert.That(text.Select(run => run.Text), Is.EqualTo(new[] { "AB", "\u2026" }));
        Assert.That(text.Select(run => run.X), Is.EqualTo(new[] { 0d, 12d }));
    }
    [Test]
    public void VisibleOverflowDoesNotEnableEllipsis()
    {
        var display = Fixture("ABCDEFG", "#line{width:23px;white-space:nowrap;text-overflow:ellipsis}");
        Assert.That(display.Runs.OfType<TextRun>().Single().Text, Is.EqualTo("ABCDEFG"));
    }
    [Test]
    public void NoWrapIntrinsicMinimumUsesWholeLineRatherThanLongestWord()
    {
        var display = Fixture("AA BB", "#host{position:relative;width:18px;height:100px}#line{position:absolute;left:0;width:auto;white-space:nowrap}", true);
        Assert.That(LineBounds(display).Width, Is.EqualTo(30));
        Assert.That(LineBounds(display).Height, Is.EqualTo(15));
    }
    [Test]
    public void PreservedWhitespaceAndBidiInputRemainExplicitUnsupportedCapabilities()
    {
        Assert.Throws<NotSupportedException>(() => Fixture("AA BB", "#line{white-space:pre}"));
        Assert.Throws<NotSupportedException>(() => Fixture("Hebrew א"));
    }
}
