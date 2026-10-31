using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class MultiFontFamilyTests
{
    // Deterministic layout contract metrics, not real font shaping.
    private sealed class Metrics(double advance, double ascent) : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) =>
            new(text.Length * (advance + letterSpacing), ascent, 2) { LineGap = 1 };
    }
    private static Document Fixture(string body, string css = "") => HtmlImporter.Import("<!doctype html><style>" +
        "html,body,div,p,input{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;appearance:none}" +
        "html{font-family:'Body Face';font-size:20px;line-height:normal}html,body{width:100%;height:100%}" +
        ".display{font-family:Heading Face}p,input{width:40px}" + css + "</style>" + body,
        "multi-font", WebUrl.Parse("res:///tests/document.html")!).Document;
    private static FlowLayout Layout(Document doc) => new(new StyleEngine(doc),
        new Dictionary<string, ITextShaper>(StringComparer.OrdinalIgnoreCase)
        { ["Body Face"] = new Metrics(5, 8), ["Heading Face"] = new Metrics(10, 15) });
    private static TextRun[] Runs(DisplayList list) => list.Runs.Select(r => r is TextEffectsRun fx ? fx.Text : r).OfType<TextRun>().ToArray();

    [TestCase("AA BB")]
    [TestCase("éé BB")]
    public void ComputedInheritedFamilySelectsWrappingStrutAndPaint(string text)
    {
        var doc = Fixture($"<p id=body>{text}</p><div class=display><p id=title>{text}</p></div><p id=again>AA</p>");
        var list = Layout(doc).Layout(doc, 400, 300);
        Assert.That(list.Boxes.Single(b => b.Element.Id == "body").Bounds.Height, Is.EqualTo(11));
        Assert.That(list.Boxes.Single(b => b.Element.Id == "title").Bounds.Height, Is.EqualTo(36));
        Assert.That(Runs(list).Select(r => (r.Text, r.FontFamily)), Is.EqualTo(new[]
            { (text, "Body Face"), (text[..2], "Heading Face"), ("BB", "Heading Face"), ("AA", "Body Face") }));
    }
    [Test]
    public void TextControlsAndEffectsRetainTheirSelectedFamily()
    {
        var doc = Fixture("<input class=display value=AB><p class=display>AB</p>", "p{text-shadow:1px 1px black}");
        var list = Layout(doc).Layout(doc, 400, 300);
        Assert.That(Runs(list).All(r => r.FontFamily == "Heading Face"), Is.True);
        Assert.That(Runs(list).First().Baseline, Is.EqualTo(15.5));
        Assert.That(list.Runs.OfType<TextEffectsRun>().Count(), Is.EqualTo(1));
    }
    [TestCase("")]
    [TestCase("display")]
    public void TextControlAndEffectRunsRetainInheritedItalicAndExplicitNormal(string familyClass)
    {
        var doc = Fixture("<div class='" + familyClass + "' style='font-style:italic'>" +
            "<input value=AB><p>AB</p><p style='font-style:normal'>AB</p></div>", "p{text-shadow:1px 1px black}");
        var list = Layout(doc).Layout(doc, 400, 300);
        var runs = Runs(list);
        Assert.That(runs.Select(run => run.FontItalic), Is.EqualTo(new[] { true, true, false }));
        Assert.That(runs.First().TextControl, Is.TypeOf<HTMLInputElement>());
        Assert.That(runs.All(run => run.FontFamily == (familyClass == "display" ? "Heading Face" : "Body Face")), Is.True);
        Assert.That(list.Runs.OfType<TextEffectsRun>().Count(), Is.EqualTo(2));
    }
    [TestCase("'Missing'")]
    [TestCase("'Missing', 'Body Face'")]
    [TestCase("'Body Face', serif")]
    public void UnknownFamiliesAndFallbackListsAreExplicitlyRefused(string family)
    {
        var doc = Fixture("<p>AB</p>", "p{font-family:" + family + "}");
        Assert.Throws<NotSupportedException>(() => Layout(doc).Layout(doc, 400, 300));
    }
    [Test]
    public void CssomFamilyMutationDoesNotRetainPreviousFamily()
    {
        var doc = Fixture("<p id=title>AA BB</p>"); var flow = Layout(doc);
        Assert.That(Runs(flow.Layout(doc, 400, 300)).Length, Is.EqualTo(1));
        ((HTMLElement)doc.GetElementById("title")!).Style.SetProperty("font-family", "'heading face'");
        var runs = Runs(flow.Layout(doc, 400, 300));
        Assert.That(runs.Length, Is.EqualTo(2));
        Assert.That(runs.All(r => r.FontFamily == "heading face"), Is.True);
    }
    private static Dictionary<string, string> Paths(Document doc) => AuthoredFontFamilies.Resolve(doc,
        new Dictionary<string, string> { ["Body Face"] = "res://mapped.ttf" });
    [Test]
    public void AuthoredFaceResolvesAgainstOwningStylesheetAndOverridesMapping()
    {
        var doc = Fixture("<p>AB</p>");
        doc.StyleSheets.Add(CSSStyleSheet.Parse("@font-face{font-family:'Body Face';src:url('../fonts/body.ttf');font-weight:400;font-style:normal}" +
            "@font-face{font-family:'Heading Face';src:url('../fonts/display.ttf')}", "theme", WebUrl.Parse("res:///assets/css/theme.css")));
        var paths = Paths(doc);
        Assert.That(paths["Body Face"], Is.EqualTo("res://assets/fonts/body.ttf"));
        Assert.That(paths["heading face"], Is.EqualTo("res://assets/fonts/display.ttf"));
    }
    [TestCase("@font-face{font-family:X;src:local(Arial)}")]
    [TestCase("@font-face{font-family:X;src:url(https://example.com/font.ttf)}")]
    [TestCase("@font-face{font-family:X;src:url(a.ttf),url(b.ttf)}")]
    [TestCase("@font-face{font-family:X;src:url(a.ttf);font-weight:700}")]
    [TestCase("@font-face{font-family:X;src:url(a.ttf)}@font-face{font-family:X;src:url(b.ttf)}")]
    [TestCase("@media all{@font-face{font-family:X;src:url(a.ttf)}}")]
    public void UnavailableSourcesAndFaceVariantsAreRefusedWithoutFallback(string css)
    {
        var doc = Fixture("<p>AB</p>");
        doc.StyleSheets.Add(CSSStyleSheet.Parse(css, "theme", WebUrl.Parse("res:///theme.css")));
        Assert.Throws<NotSupportedException>(() => Paths(doc));
    }
}
