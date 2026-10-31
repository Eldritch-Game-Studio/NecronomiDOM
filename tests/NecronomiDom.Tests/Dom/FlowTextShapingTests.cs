using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class FlowTextShapingTests
{
    // This spy specifies deterministic test metrics, not native font or browser raster behavior.
    private sealed class StyledMetrics : ITextShaper
    {
        public readonly List<(string Text, int Size, double Weight, double Spacing)> Calls = new();
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
        {
            Calls.Add((text, fontSize, fontWeight, letterSpacing));
            bool bold = fontWeight >= 700;
            return new(text.Length * ((bold ? 7 : 5) + letterSpacing), bold ? 9 : 8, 2) { LineGap = 2 };
        }
    }
    private static (Document Doc, StyleEngine Styles, FlowLayout Flow, StyledMetrics Metrics) Fixture(string text, string css = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,div{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;font-family:monospace;font-size:10px;line-height:normal;appearance:none}" +
            "html,body{width:100%;height:100%;background:white}body{font-weight:700;letter-spacing:3px}#line{width:100px}" + css +
            "</style><div id=host><div id=line>" + text + "</div></div>",
            "text-shaping", WebUrl.Parse("res:///text-shaping.html")!).Document;
        var styles = new StyleEngine(doc);
        var metrics = new StyledMetrics();
        return (doc, styles, new FlowLayout(styles, metrics, "monospace"), metrics);
    }
    private static TextRun[] TextRuns(DisplayList display) => display.Runs.Select(run => run switch
    {
        TextRun text => text,
        TextEffectsRun effects => effects.Text,
        _ => null
    }).OfType<TextRun>().ToArray();
    private static Rect LineBounds(DisplayList display) => display.Boxes.Single(box => box.Element.Id == "line").Bounds;
    private static void AssertStyled(StyledMetrics metrics, double weight, double spacing)
    {
        Assert.That(metrics.Calls, Is.Not.Empty);
        Assert.That(metrics.Calls.All(call => call.Weight == weight && call.Spacing == spacing), Is.True);
    }

    [Test]
    public void InheritedComputedWeightAndTrackingReachStrutMeasurementsAndPaint()
    {
        var f = Fixture("AB");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        AssertStyled(f.Metrics, 700, 3);
        Assert.That(f.Metrics.Calls.Any(call => call.Text == " "), Is.True);
        Assert.That(LineBounds(display).Height, Is.EqualTo(13));
        var run = TextRuns(display).Single();
        Assert.That((run.FontWeight, run.LetterSpacing), Is.EqualTo((700d, 3d)));
        Assert.That(run.Baseline, Is.EqualTo(10));
    }
    [Test]
    public void ExplicitNormalOverridesInheritedBoldAndTracking()
    {
        var f = Fixture("AB", "#line{font-weight:normal;letter-spacing:normal}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        AssertStyled(f.Metrics, 400, 0);
        Assert.That(LineBounds(display).Height, Is.EqualTo(12));
        Assert.That((TextRuns(display).Single().FontWeight, TextRuns(display).Single().LetterSpacing), Is.EqualTo((400d, 0d)));
    }
    [Test]
    public void ComputedNumbersAreNotRoundedThroughCssomSerialization()
    {
        var f = Fixture("AB", "#line{font-weight:700.123456789;letter-spacing:3.123456789px}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        var run = TextRuns(display).Single();
        Assert.That(run.FontWeight, Is.EqualTo(700.123456789));
        Assert.That(run.LetterSpacing, Is.EqualTo(3.123456789));
        AssertStyled(f.Metrics, run.FontWeight, run.LetterSpacing);
    }
    [Test]
    public void WrappingUsesTheSameStyledMeasurementsAsDrawing()
    {
        var f = Fixture("AA BB", "#line{width:28px}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        Assert.That(TextRuns(display).Select(run => run.Text), Is.EqualTo(new[] { "AA", "BB" }));
        Assert.That(LineBounds(display).Height, Is.EqualTo(26));
        AssertStyled(f.Metrics, 700, 3);
    }
    [Test]
    public void CenteringUsesStyledAdvanceWithoutChangingGlyphCoordinates()
    {
        var f = Fixture("AB", "#line{text-align:center}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        Assert.That(TextRuns(display).Single().X, Is.EqualTo(40));
        AssertStyled(f.Metrics, 700, 3);
    }
    [Test]
    public void EllipsisPrefixMarkerAndGlyphEffectsRetainTheSameShapingParameters()
    {
        var f = Fixture("ABCDE", "#line{width:35px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;-webkit-text-stroke:1px black}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        var runs = TextRuns(display);
        Assert.That(runs.Select(run => run.Text), Is.EqualTo(new[] { "AB", "\u2026" }));
        Assert.That(runs.Select(run => run.X), Is.EqualTo(new[] { 0d, 20d }));
        Assert.That(display.Runs.OfType<TextEffectsRun>().Count(), Is.EqualTo(2));
        Assert.That(runs.All(run => run.FontWeight == 700 && run.LetterSpacing == 3), Is.True);
        Assert.That(f.Metrics.Calls.Any(call => call.Text == "\u2026"), Is.True);
        AssertStyled(f.Metrics, 700, 3);
    }
    [Test]
    public void AbsoluteIntrinsicNoWrapWidthUsesStyledWholeLine()
    {
        var f = Fixture("AA BB", "#host{position:relative;width:40px;height:100px}#line{position:absolute;left:0;width:auto;white-space:nowrap}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        Assert.That(LineBounds(display).Width, Is.EqualTo(50));
        AssertStyled(f.Metrics, 700, 3);
    }
    [Test]
    public void FlexHeightAndIntrinsicSizingMeasureActualStyledText()
    {
        var f = Fixture("AA BB", "#host{display:flex;width:45px;align-items:flex-start}#line{width:auto;min-width:0;flex:0 1 auto}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        Assert.That(LineBounds(display).Width, Is.EqualTo(45));
        Assert.That(LineBounds(display).Height, Is.EqualTo(26));
        AssertStyled(f.Metrics, 700, 3);
    }
    [Test]
    public void StyleMutationAndRestoreUpdateAllMetricsAndRetainedRunParameters()
    {
        var f = Fixture("AB");
        var initial = f.Flow.Layout(f.Doc, 400, 300);
        var line = f.Doc.GetElementById("line")!;
        line.SetAttribute("style", "font-weight:400;letter-spacing:1px");
        f.Styles.Invalidate();
        f.Metrics.Calls.Clear();
        var changed = f.Flow.Layout(f.Doc, 400, 300);
        AssertStyled(f.Metrics, 400, 1);
        Assert.That(LineBounds(changed).Height, Is.EqualTo(12));
        Assert.That((TextRuns(changed).Single().FontWeight, TextRuns(changed).Single().LetterSpacing), Is.EqualTo((400d, 1d)));
        line.RemoveAttribute("style");
        f.Styles.Invalidate();
        f.Metrics.Calls.Clear();
        var restored = f.Flow.Layout(f.Doc, 400, 300);
        AssertStyled(f.Metrics, 700, 3);
        Assert.That(TextRuns(initial).Single(), Is.EqualTo(TextRuns(restored).Single()));
    }
    [Test]
    public void InheritedItalicMutationAndRestorePreserveLogicalGeometryAndOtherRunStyles()
    {
        var f = Fixture("AA BB", "body{font-style:italic}#line{width:28px}");
        var initial = f.Flow.Layout(f.Doc, 400, 300);
        var initialRuns = TextRuns(initial);
        Assert.That(initialRuns.Select(run => run.Text), Is.EqualTo(new[] { "AA", "BB" }));
        Assert.That(initialRuns.All(run => run.FontItalic && run.FontWeight == 700 && run.LetterSpacing == 3), Is.True);
        var line = (HTMLElement)f.Doc.GetElementById("line")!;
        line.Style.SetProperty("font-style", "normal");
        var normal = f.Flow.Layout(f.Doc, 400, 300);
        Assert.That(TextRuns(normal), Is.EqualTo(initialRuns.Select(run => run with { FontItalic = false })));
        Assert.That(LineBounds(normal), Is.EqualTo(LineBounds(initial)));
        line.Style.RemoveProperty("font-style");
        Assert.That(TextRuns(f.Flow.Layout(f.Doc, 400, 300)), Is.EqualTo(initialRuns));
    }
    [Test]
    public void ItalicEllipsisAndGlyphEffectsRetainStyleOnPrefixAndMarker()
    {
        var f = Fixture("ABCDE", "#line{font-style:italic;width:35px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;-webkit-text-stroke:1px black}");
        var display = f.Flow.Layout(f.Doc, 400, 300);
        var runs = TextRuns(display);
        Assert.That(runs.Select(run => run.Text), Is.EqualTo(new[] { "AB", "\u2026" }));
        Assert.That(runs.All(run => run.FontItalic && run.FontWeight == 700 && run.LetterSpacing == 3), Is.True);
        Assert.That(display.Runs.OfType<TextEffectsRun>().Count(), Is.EqualTo(2));
    }
    [TestCase("oblique")]
    [TestCase("oblique 10deg")]
    [TestCase("left")]
    [TestCase("right")]
    public void UnimplementedFontStylesRemainExplicitlyRefused(string style)
    {
        var f = Fixture("AB", "#line{font-style:" + style + "}");
        var error = Assert.Throws<NotSupportedException>(() => f.Flow.Layout(f.Doc, 400, 300));
        Assert.That(error!.Message, Does.Contain("font-style"));
    }
}
