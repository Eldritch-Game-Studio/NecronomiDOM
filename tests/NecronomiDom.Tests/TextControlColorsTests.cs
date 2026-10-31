using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

[TestFixture]
public sealed class TextControlColorsTests
{
    private static CSSStyleDeclaration Values(string css)
    {
        var document = HtmlImporter.Import("<!doctype html><style>body { --accent:#e0b46b; --selection:#8e6535; }"
            + "input { caret-color:var(--accent); --necronomidom-selection-color:var(--selection); " + css
            + " }</style><input>", "text-colors", WebUrl.Parse("res:///text-colors.html")!).Document;
        return new StyleEngine(document).GetComputedStyle(document.QuerySelector("input")!);
    }

    [Test]
    public void ComputedCaretAndInheritedVariableSelectionReachThePaintResolver()
    {
        var values = Values("");
        foreach (var declaration in values.EnumerateLonghands())
            if (declaration.Name is "caret-color" or "--necronomidom-selection-color")
                TestContext.WriteLine(declaration.Name + ": " + declaration.Value.GetType().Name + " " + declaration.Value.Serialize());
        var colors = TextControlColors.Resolve(values, new Rgba(0, 0, 0, 1));
        Assert.That(colors.Caret, Is.EqualTo(new Rgba(224f / 255, 180f / 255, 107f / 255, 1)));
        Assert.That(colors.Selection, Is.EqualTo(new Rgba(142f / 255, 101f / 255, 53f / 255, 1)));
    }

    [Test]
    public void CaretVariableAndSelectionCurrentcolorResolveAgainstForeground()
    {
        var colors = TextControlColors.Resolve(Values("caret-color:var(--accent); --necronomidom-selection-color:currentcolor"),
            new Rgba(0, 1, 0, 1));
        Assert.That(colors.Caret, Is.EqualTo(new Rgba(224f / 255, 180f / 255, 107f / 255, 1)));
        Assert.That(colors.Selection, Is.EqualTo(new Rgba(0, 1, 0, 1)));
    }

    [TestCase("auto")]
    [TestCase("currentcolor")]
    public void AutomaticAndCurrentcolorCaretUseForeground(string color)
    {
        var colors = TextControlColors.Resolve(Values("color:blue; caret-color:" + color), new Rgba(0, 0, 1, 1));
        Assert.That(colors.Caret, Is.EqualTo(new Rgba(0, 0, 1, 1)));
    }

    [Test]
    public void ParsedColorAstIsResolvedWithoutRequiringComputedValueColorWrapper()
    {
        var colors = TextControlColors.Resolve(CSSStyleDeclaration.Parse("caret-color:#e0b46b;--necronomidom-selection-color:#8e6535"), default);
        Assert.That(colors.Caret, Is.EqualTo(new Rgba(224f / 255, 180f / 255, 107f / 255, 1)));
        Assert.That(colors.Selection, Is.EqualTo(new Rgba(142f / 255, 101f / 255, 53f / 255, 1)));
    }

    [Test]
    public void SolidSwatchesPreserveResolvedColorAndAlphaAcrossEdgeInterpolation()
    {
        var colors = TextControlColors.Resolve(Values(""), default);
        foreach (var color in new[] { colors.Caret, colors.Selection, new Rgba(1, 0, 0, 0.5f) })
        {
            var run = TextControlColors.Swatch(new Rect(78.25, 9, 1, 33), color);
            Assert.That(run.Style.Background, Is.EqualTo(color));
            Assert.That(run.Style.Border, Is.EqualTo(color), "Zero-width border still participates in the box shader edge interpolation.");
        }
    }
}
