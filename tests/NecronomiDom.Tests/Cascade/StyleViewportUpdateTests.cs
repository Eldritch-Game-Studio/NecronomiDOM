using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class StyleViewportUpdateTests
{
    private static (Document Document, StyleEngine Styles) Load(string css)
    {
        var doc = HtmlImporter.Import("<div id=x></div>", "viewport", WebUrl.Parse("res:///viewport.html")!).Document;
        doc.AdoptedStyleSheets.Add(CSSStyleSheet.Parse(css));
        return (doc, new StyleEngine(doc));
    }
    private static void Compare(Document document, StyleEngine actual, Document control, StyleEngine reference)
    {
        var left = actual.GetComputedStyle(document.GetElementById("x")!);
        var right = reference.GetComputedStyle(control.GetElementById("x")!);
        Assert.That(left.Length, Is.EqualTo(right.Length));
        for (int i = 0; i < right.Length; i++)
        {
            string name = right.Item(i);
            Assert.That(left.Item(i), Is.EqualTo(name));
            Assert.That(left.GetPropertyValue(name), Is.EqualTo(right.GetPropertyValue(name)), name);
        }
    }

    [Test]
    public void PixelOnlyViewportResizeRetainsAllComputedValuesWithoutWarmAllocation()
    {
        // A pixel-only viewport change must not recascade; viewport changes still reach layout.
        var (doc, styles) = Load("#x{width:30px;height:50%;font-size:16px;color:navy}");
        styles.UpdateViewport(800, 600);
        RuntimeStyleProfile.Validate(doc);
        var element = doc.GetElementById("x")!;
        for (int i = 0; i < 4; i++) { styles.UpdateViewport(800 + (i & 1), 600); styles.GetComputedStyle(element); }
        var media = styles.Media;
        long computed = styles.ComputedEntryCount;
        long allocated = AllocationWindow.Measure(100, i =>
        {
            RuntimeStyleProfile.Validate(doc);
            styles.UpdateViewport(800 + (i & 1), 600);
            _ = styles.GetComputedStyle(element).GetSpecifiedValue("width");
        });
        Assert.That(allocated, Is.Zero);
        Assert.That(styles.ComputedEntryCount, Is.EqualTo(computed));
        Assert.That(styles.Media, Is.SameAs(media));
        Assert.That(styles.Media.Width, Is.EqualTo(801));
    }

    [TestCase("10vw")]
    [TestCase("10svw")]
    [TestCase("10lvw")]
    [TestCase("10dvw")]
    [TestCase("10cqw")]
    [TestCase("calc(5vw + 10px)")]
    public void ActualViewportAndContainerFallbackUnitsStillMatchFullRecascade(string width)
    {
        string css = ":root{font-size:16px}#x{width:" + width + ";height:20px}";
        var (doc, styles) = Load(css); var (control, reference) = Load(css);
        styles.UpdateViewport(800, 600); reference.Media = new MediaEnvironment { Width = 800, Height = 600 };
        Compare(doc, styles, control, reference);
        long computed = styles.ComputedEntryCount;
        styles.UpdateViewport(1000, 700); reference.Media = new MediaEnvironment { Width = 1000, Height = 700 };
        Compare(doc, styles, control, reference);
        Assert.That(styles.ComputedEntryCount, Is.GreaterThan(computed));
    }

    [Test]
    public void StableMediaTruthReusesButCrossingTheSameQueryRecascades()
    {
        const string css = "#x{color:navy}@media(min-width:500px){#x{color:red}}";
        var (doc, styles) = Load(css); var (control, reference) = Load(css);
        styles.UpdateViewport(800, 600); reference.Media = new MediaEnvironment { Width = 800, Height = 600 };
        Compare(doc, styles, control, reference);
        long computed = styles.ComputedEntryCount;
        styles.UpdateViewport(900, 600); reference.Media = new MediaEnvironment { Width = 900, Height = 600 };
        Compare(doc, styles, control, reference);
        Assert.That(styles.ComputedEntryCount, Is.EqualTo(computed));
        styles.UpdateViewport(400, 600); reference.Media = new MediaEnvironment { Width = 400, Height = 600 };
        Compare(doc, styles, control, reference);
        Assert.That(styles.ComputedEntryCount, Is.GreaterThan(computed));
    }

    [Test]
    public void FeatureMutationAndStylesheetRevisionCannotBeHiddenByViewportReuse()
    {
        const string css = "#x{color:navy}@media(prefers-color-scheme:dark){#x{color:red}}";
        var (doc, styles) = Load(css); var (control, reference) = Load(css);
        styles.UpdateViewport(800, 600); reference.Media = new MediaEnvironment { Width = 800, Height = 600 };
        Compare(doc, styles, control, reference);
        styles.Media.Features["prefers-color-scheme"] = "dark";
        styles.UpdateViewport(900, 600);
        var media = new MediaEnvironment { Width = 900, Height = 600 }; media.Features["prefers-color-scheme"] = "dark";
        reference.Media = media;
        Compare(doc, styles, control, reference);
        ((CSSStyleRule)doc.AdoptedStyleSheets[0].CssRules[0]).Style.SetProperty("background-color", "green");
        ((CSSStyleRule)control.AdoptedStyleSheets[0].CssRules[0]).Style.SetProperty("background-color", "green");
        styles.UpdateViewport(950, 600);
        var updated = new MediaEnvironment { Width = 950, Height = 600 }; updated.Features["prefers-color-scheme"] = "dark";
        reference.Media = updated;
        Compare(doc, styles, control, reference);
    }

    [Test]
    public void PriorExternalContextKeepsConservativeResizeAfterProviderWithdrawal()
    {
        var (doc, styles) = Load("#x{width:2em}"); var (control, reference) = Load("#x{width:2em}");
        styles.ValueContext = _ => new ComputedValueContext { Font = new ComputedFontMetrics(Size: 30) };
        reference.ValueContext = styles.ValueContext;
        styles.UpdateViewport(800, 600); reference.Media = new MediaEnvironment { Width = 800, Height = 600 };
        Compare(doc, styles, control, reference);
        styles.ValueContext = null; reference.ValueContext = null;
        styles.UpdateViewport(900, 600); reference.Media = new MediaEnvironment { Width = 900, Height = 600 };
        Compare(doc, styles, control, reference);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PriorMediaProvidersKeepConservativeResizeAfterWithdrawal(bool customMedia)
    {
        var (doc, styles) = Load("#x{width:30px}");
        if (customMedia) styles.Media.CustomMedia["--external"] = new BooleanCondition(true);
        else styles.Media.UnitScale = _ => null;
        styles.UpdateViewport(800, 600);
        var element = doc.GetElementById("x")!;
        styles.GetComputedStyle(element);
        long computed = styles.ComputedEntryCount;
        styles.Media.CustomMedia.Clear(); styles.Media.UnitScale = null;
        styles.UpdateViewport(900, 600);
        styles.GetComputedStyle(element);
        Assert.That(styles.ComputedEntryCount, Is.GreaterThan(computed));
    }

    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }
    [Test]
    public void RetainedComputedPercentagesStillLayOutAgainstTheNewViewport()
    {
        var (doc, styles) = Load("html,body,div{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;" +
            "font-family:monospace;font-size:16px;line-height:20px;appearance:none}" +
            "html,body{width:100%;height:100%}#x{width:50%;height:50%}");
        var flow = new FlowLayout(styles, new Metrics(), "monospace");
        styles.UpdateViewport(800, 600);
        var first = flow.Layout(doc, 800, 600);
        var element = doc.GetElementById("x")!;
        Rect oldBounds = first.Boxes.Find(box => ReferenceEquals(box.Element, element))!.Bounds;
        long computed = styles.ComputedEntryCount;
        styles.UpdateViewport(1000, 700);
        var next = flow.Layout(doc, 1000, 700);
        Rect newBounds = next.Boxes.Find(box => ReferenceEquals(box.Element, element))!.Bounds;
        Assert.That(oldBounds, Is.EqualTo(new Rect(0, 0, 400, 300)));
        Assert.That(newBounds, Is.EqualTo(new Rect(0, 0, 500, 350)));
        Assert.That(styles.ComputedEntryCount, Is.EqualTo(computed));
    }
}
