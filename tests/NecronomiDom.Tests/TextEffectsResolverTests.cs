using System;
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
    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void TypedEffectsRejectNonfiniteOrNegativeStroke(double width) => Assert.Throws<ArgumentOutOfRangeException>(
        () => new TextEffectsStyle(width, default, default));
}
