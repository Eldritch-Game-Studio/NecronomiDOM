using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class UnicodeFlowTextTests
{
    private sealed class Metrics : ITextShaper
    {
        public TextMetrics Measure(string text, int size, double weight = 400, double spacing = 0, bool italic = false)
            => new(text.Length * 8, 12, 4);
    }

    private static DisplayList Layout(string text, int width, bool flex = false)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html,body,p{display:block;margin:0;padding:0;border:0;" +
            "font-family:monospace;font-size:16px;line-height:20px}p{width:" + width + "px}" +
            (flex ? "body{display:flex;flex-direction:column;align-items:flex-start}" : "") +
            "</style><p id=message></p>", "unicode", WebUrl.Parse("res:///unicode.html")!).Document;
        doc.GetElementById("message")!.TextContent = text;
        return new FlowLayout(new StyleEngine(doc), new Metrics(), "monospace").Layout(doc, 1280, 720);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BreakBothDashWrapsOnEitherSideAndFlexUsesIdenticalHeight(bool flex)
    {
        var display = Layout("A—B", 16, flex);
        Assert.That(display.Runs.OfType<TextRun>().Select(r => r.Text), Is.EqualTo(new[] { "A—", "B" }));
        Assert.That(display.Boxes.Single(b => b.Element.Id == "message").Bounds.Height, Is.EqualTo(40));
    }

    [TestCase("A——B", new[] { "A", "——", "B" })]
    [TestCase("A— —B", new[] { "A—", "—B" })]
    public void DashPairsUseBrowserAdjacentAndSpacedBreaks(string text, string[] expected)
        => Assert.That(Layout(text, 16).Runs.OfType<TextRun>().Select(r => r.Text), Is.EqualTo(expected));

    [Test]
    public void UnicodeLettersAndExistingWhitespaceCollapseArePreserved()
        => Assert.That(Layout("  Café\t\n —  earnings  ", 300).Runs.OfType<TextRun>().Single().Text,
            Is.EqualTo("Café — earnings"));

    [Test]
    public void ExistingAsciiSpaceWrappingRemainsExact()
        => Assert.That(Layout("one two three", 56).Runs.OfType<TextRun>().Select(r => r.Text),
            Is.EqualTo(new[] { "one two", "three" }));

    [TestCase("Hebrew א")]
    [TestCase("Arabic ع")]
    [TestCase("digits ١")]
    [TestCase("LTR\u200Ftext")]
    [TestCase("LTR\u202Etext")]
    [TestCase("LTR\u2066text")]
    public void RtlBidiControlsAndInvalidScalarsRemainRefused(string text)
        => Assert.That(() => Layout(text, 300), Throws.TypeOf<NotSupportedException>());

    [Test]
    public void InvalidUtf16ScalarRemainsRefused()
        => Assert.That(() => Layout("invalid" + (char)0xD800, 300),
            Throws.TypeOf<NotSupportedException>());
}
