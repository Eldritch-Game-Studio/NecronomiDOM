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
public sealed class FlowRetainedNumericTextTests
{
    private sealed class Metrics : IInlineTextShaper
    {
        internal bool Proportional, VariableAscent;
        public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) =>
            new(InlineAdvance(text, text.Length, size), VariableAscent && text.Contains('9') ? 13 : 12, 4);
        public double InlineAdvance(string text, int offset, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
        {
            double width = 0;
            for (int i = 0; i < offset; i++) width += Proportional && text[i] == '1' ? 4 : 8;
            return width;
        }
        public Rect InlineInkBounds(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
        {
            double extra = 0;
            foreach (char c in text) if (c is >= '0' and <= '9') { extra = (c - '0') / 10.0; break; }
            return new Rect(-extra, -12, Measure(text, size).Width + extra * 2, 16);
        }
    }

    private static (Document Doc, StyleEngine Styles, FlowLayout Flow, HTMLElement Target, Metrics Font) Load(string text = "SPD 1.0", string css = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;font-family:monospace;font-size:16px;line-height:20px}" +
            "html,body{width:100%;height:100%}#target{width:180px;height:30px;text-align:right}#other{width:30px;height:20px;background:red}" + css +
            "</style><div id=target>" + text + "</div><div id=other>Other</div>", "numeric-text", WebUrl.Parse("res:///numeric-text.html")!).Document;
        var styles = new StyleEngine(doc); var font = new Metrics();
        return (doc, styles, new FlowLayout(styles, font, "monospace"), (HTMLElement)doc.GetElementById("target")!, font);
    }

    private static void SameDisplay(DisplayList actual, DisplayList expected)
    {
        Assert.That(actual.Runs, Is.EqualTo(expected.Runs));
        Assert.That(actual.Boxes.Select(b => b.Bounds), Is.EqualTo(expected.Boxes.Select(b => b.Bounds)));
        Assert.That(actual.Boxes.Select(b => b.ScrollOverflow), Is.EqualTo(expected.Boxes.Select(b => b.ScrollOverflow)));
        for (int y = 0; y < 70; y += 4)
            for (int x = 0; x < 240; x += 8)
                Assert.That(actual.HitTest(x + .2, y + .3)?.Id, Is.EqualTo(expected.HitTest(x + .2, y + .3)?.Id));
    }

    [TestCase("1.2 s", "1.3 s", false)]
    [TestCase("1.2 s", "1.3 s", true)]
    [TestCase("SPD 12.3", "SPD 98.7", false)]
    [TestCase("SPD 12.3", "SPD 98.7", true)]
    [TestCase("18%", "91%", false)]
    [TestCase("18%", "91%", true)]
    public void SameShapingBoundariesPreserveCanonicalPaintBoxesAndHits(string before, string next, bool effects)
    {
        var (doc, styles, flow, target, font) = Load(before, effects ? "#target{text-shadow:1px 2px 2px black;-webkit-text-stroke:1px blue}" : "");
        var list = flow.Layout(doc, 240, 160);
        var unrelated = list.Boxes.Single(b => b.Element.Id == "other");
        target.TextContent = next;
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out var update), Is.True);
        flow.ApplyNumericTextUpdates(new[] { update });
        SameDisplay(list, new FlowLayout(styles, font, "monospace").Layout(doc, 240, 160));
        Assert.That(list.Boxes.Single(b => b.Element.Id == "other"), Is.SameAs(unrelated));
        var run = list.Runs.OfType<TextRun>().FirstOrDefault(r => r.Owner == target) ?? list.Runs.OfType<TextEffectsRun>().Single(r => r.Text.Owner == target).Text;
        Assert.That(run.InlineFragment, Is.Null, "Canonical plain blocks do not invent inline fragments.");
        Assert.That(run.ShapingText, Is.Null);
    }

    [Test]
    public void CompleteInlineFragmentKeepsCurrentNodeAndCanonicalInkClip()
    {
        var (doc, styles, flow, target, font) = Load("12.0 s");
        var span = doc.CreateElement("span"); span.TextContent = target.TextContent;
        target.TextContent = ""; target.AppendChild(span);
        var list = flow.Layout(doc, 240, 160); span.TextContent = "98.7 s";
        Assert.That(flow.TryPrepareNumericTextUpdate(span, out var update), Is.True);
        flow.ApplyNumericTextUpdates(new[] { update });
        SameDisplay(list, new FlowLayout(styles, font, "monospace").Layout(doc, 240, 160));
        Assert.That(list.Runs.OfType<TextRun>().Single(r => r.Owner == span).InlineFragment!.Node, Is.SameAs(span.FirstChild));
    }

    [Test]
    public void RepeatedCharacterDataUpdatesStayCanonicalWithChangedInkBounds()
    {
        var (doc, styles, flow, target, font) = Load("10.0 s", "#target{text-shadow:1px 1px 2px black}");
        var list = flow.Layout(doc, 240, 160);
        for (int i = 0; i < 20; i++)
        {
            ((Text)target.FirstChild!).Data = "1" + i % 10 + "." + (9 - i % 10) + " s";
            Assert.That(flow.TryPrepareNumericTextUpdate(target, out var update), Is.True);
            flow.ApplyNumericTextUpdates(new[] { update });
            SameDisplay(list, new FlowLayout(styles, font, "monospace").Layout(doc, 240, 160));
        }
    }

    [TestCase("SPD 12.0")]
    [TestCase("SPD A.0")]
    [TestCase("SPD ١.٠")]
    [TestCase("")]
    public void StructuralNonNumericOrLengthChangesUseFullLayout(string next)
    {
        var (doc, _, flow, target, _) = Load(); flow.Layout(doc, 240, 160); target.TextContent = next;
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out _), Is.False);
    }

    [Test]
    public void EqualTotalWidthWithDifferentPrefixAdvancesIsNotRetained()
    {
        var (doc, _, flow, target, font) = Load("12"); font.Proportional = true;
        flow.Layout(doc, 240, 160); target.TextContent = "21";
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out _), Is.False);
    }

    [Test]
    public void ChangedAscentIsNotRetained()
    {
        var (doc, _, flow, target, font) = Load("1"); font.VariableAscent = true;
        flow.Layout(doc, 240, 160); target.TextContent = "9";
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out _), Is.False);
    }

    [Test]
    public void WrappedOrMixedInlineTextIsNotRetained()
    {
        var (doc, _, flow, target, _) = Load("12 34 56", "#target{width:24px}");
        flow.Layout(doc, 240, 160); target.TextContent = "21 43 65";
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out _), Is.False);
        target.AppendChild(doc.CreateElement("span"));
        flow.Layout(doc, 240, 160);
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out _), Is.False);
    }

    [Test]
    public void NewLayoutRejectsPreparedGeneration()
    {
        var (doc, _, flow, target, _) = Load(); flow.Layout(doc, 240, 160); target.TextContent = "SPD 2.0";
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out var update), Is.True);
        flow.Layout(doc, 240, 160);
        Assert.Throws<InvalidOperationException>(() => flow.ApplyNumericTextUpdates(new[] { update }));
    }

    [Test]
    public void ExplicitStyleInvalidationCannotBeConsumedAsTextOnly()
    {
        var (doc, styles, flow, target, _) = Load(); flow.Layout(doc, 240, 160);
        target.TextContent = "SPD 2.0"; styles.Invalidate();
        Assert.That(flow.TryPrepareNumericTextUpdate(target, out _), Is.False);
    }

    [Test]
    public void ObserverSeparatesTextCandidatesButStillMarksElementStructureDirty()
    {
        var (doc, _, _, target, _) = Load(); int dirty = 0; var text = new HashSet<Element>();
        using var observer = new DocumentInvalidation(doc, () => dirty++, numericTextChanged: e => text.Add(e));
        target.TextContent = "SPD 2.0";
        Assert.That(text, Does.Contain(target)); Assert.That(dirty, Is.Zero);
        target.AppendChild(doc.CreateElement("div"));
        Assert.That(dirty, Is.GreaterThan(0));
    }
}
