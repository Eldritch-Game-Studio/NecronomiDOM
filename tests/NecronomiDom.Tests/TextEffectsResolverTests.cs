using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

[TestFixture]
public sealed class TextEffectsResolverTests
{
    private static CSSStyleDeclaration Values(string css, string parent = "")
    {
        var document = HtmlImporter.Import("<!doctype html><div style='" + parent + "'><p style='" + css
            + "'>Caption</p></div>", "glyph-effects", WebUrl.Parse("res:///glyph-effects.html")!).Document;
        return new StyleEngine(document).GetComputedValues(document.QuerySelector("p")!);
    }
    private sealed class FixedMetrics : IInlineTextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) =>
            new(text.Length * 6, 11, 4);
        public double InlineAdvance(string text, int offset, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) =>
            offset * 6;
        public Rect InlineInkBounds(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) =>
            new(0, -11, Measure(text, fontSize, fontWeight, letterSpacing, italic).Width, 15);
    }
    [Test]
    public void RepeatedLayoutProducesEqualEffectRunsAndInlineOffsets()
    {
        var document = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,p{display:block;margin:0;padding:0;font-family:monospace;font-size:10px}" +
            "p{text-shadow:1px 1px 2px black}</style><p>Caption <span>with</span> shadow</p>",
            "retained-effects", WebUrl.Parse("res:///retained-effects.html")!).Document;
        var layout = new FlowLayout(new StyleEngine(document), new FixedMetrics(), "monospace");
        var first = layout.Layout(document, 400, 300).Runs.OfType<TextEffectsRun>().ToArray();
        var second = layout.Layout(document, 400, 300).Runs.OfType<TextEffectsRun>().ToArray();
        Assert.That(first, Is.Not.Empty);
        Assert.That(second, Is.EqualTo(first), "New shadow/offset storage must not replace identical native glyph targets.");
        Assert.That(second.Select(run => run.GetHashCode()), Is.EqualTo(first.Select(run => run.GetHashCode())));
        document.QuerySelector("p")!.TextContent = "A different caption";
        var changed = layout.Layout(document, 400, 300).Runs.OfType<TextEffectsRun>().ToArray();
        Assert.That(changed, Is.Not.EqualTo(first), "Changed text must still invalidate the paint.");
    }
    [TestCase(1)]
    [TestCase(2)]
    public void OutlineAndBlurredOffsetShadowDecodeFromActualComputedAst(int width)
    {
        var effects = TextEffectsResolver.Resolve(Values("color:white;--outline:" + width
            + "px;-webkit-text-stroke-width:var(--outline);-webkit-text-stroke-color:black;text-shadow:1px 1px 2px black"));
        Assert.That(effects.StrokeWidth, Is.EqualTo(width));
        Assert.That(effects.StrokeColor, Is.EqualTo(new Rgba(0, 0, 0, 1)));
        Assert.That(effects.Shadows.Length, Is.EqualTo(1));
        Assert.That(effects.Shadows.Span[0], Is.EqualTo(new TextShadow(1, 1, 2, new Rgba(0, 0, 0, 1))));
        Assert.That(effects.HasEffects, Is.True);
    }
    [Test]
    public void InheritedStrokeAndShadowResolveCurrentColorOnActualComputedValues()
    {
        var effects = TextEffectsResolver.Resolve(Values("", "color:red;-webkit-text-stroke-width:1px;text-shadow:2px -3px"));
        Assert.That(effects.StrokeColor, Is.EqualTo(new Rgba(1, 0, 0, 1)));
        Assert.That(effects.Shadows.Span[0], Is.EqualTo(new TextShadow(2, -3, 0, new Rgba(1, 0, 0, 1))));
    }
    [Test]
    public void MultipleShadowsKeepCssTopmostFirstOrderAndUnserializedOffsets()
    {
        var effects = TextEffectsResolver.Resolve(Values("text-shadow:0.123456789px -2px 3px rgb(255 0 0 / .5),4px 5px blue"));
        Assert.That(effects.Shadows.Length, Is.EqualTo(2));
        Assert.That(effects.Shadows.Span[0], Is.EqualTo(new TextShadow(.123456789, -2, 3, new Rgba(1, 0, 0, .5f))));
        Assert.That(effects.Shadows.Span[1], Is.EqualTo(new TextShadow(4, 5, 0, new Rgba(0, 0, 1, 1))));
    }
    [Test]
    public void OrdinaryTextHasNoEffectPrimitiveOrShadowAllocation()
    {
        var effects = TextEffectsResolver.Resolve(Values("color:green"));
        Assert.That(effects.HasEffects, Is.False);
        Assert.That(effects.Shadows.IsEmpty, Is.True);
    }
    [Test]
    public void EquivalentResolvedEffectsRetainValueEqualityAcrossLayoutPasses()
    {
        const string css = "-webkit-text-stroke-width:1px;-webkit-text-stroke-color:black;text-shadow:1px 1px 2px black,2px 3px 4px red";
        var first = TextEffectsResolver.Resolve(Values(css));
        var second = TextEffectsResolver.Resolve(Values(css));
        Assert.That(first.Shadows.Equals(second.Shadows), Is.False, "Separate computed passes own separate arrays.");
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
    }
    [Test]
    public void EffectEqualityIncludesStrokeAndOrderedShadowValues()
    {
        var black = new Rgba(0, 0, 0, 1);
        var red = new Rgba(1, 0, 0, 1);
        var first = new TextShadow(1, 2, 3, black);
        var second = new TextShadow(4, 5, 6, red);
        var original = new TextEffectsStyle(1, black, new[] { first, second });
        Assert.Multiple(() =>
        {
            Assert.That(original, Is.Not.EqualTo(new TextEffectsStyle(2, black, new[] { first, second })));
            Assert.That(original, Is.Not.EqualTo(new TextEffectsStyle(1, red, new[] { first, second })));
            Assert.That(original, Is.Not.EqualTo(new TextEffectsStyle(1, black, new[] { second, first })));
            Assert.That(original, Is.Not.EqualTo(new TextEffectsStyle(1, black, new[] { first with { BlurRadius = 4 }, second })));
            Assert.That(original, Is.Not.EqualTo(new TextEffectsStyle(1, black, new[] { first })));
            Assert.That(new TextEffectsStyle(0, black, default),
                Is.EqualTo(new TextEffectsStyle(0, black, Array.Empty<TextShadow>())));
        });
    }
    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void TypedEffectsRejectNonfiniteOrNegativeStroke(double width) => Assert.Throws<ArgumentOutOfRangeException>(
        () => new TextEffectsStyle(width, default, default));
}
