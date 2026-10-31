using System.Linq;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    [Test]
    public void StrokeAndShadowWrapTheOriginalGlyphRunWithoutChangingLineGeometry()
    {
        var (_, input, display) = Fixture("<div id=label>CAPTION</div>", """
            #label{width:120px;color:white;-webkit-text-stroke-width:2px;-webkit-text-stroke-color:black;
                text-shadow:1px 1px 2px black;white-space:nowrap}
            """);
        using var owner = input;
        var run = display.Runs.OfType<TextEffectsRun>().Single();
        Assert.That(run.Text.Text, Is.EqualTo("CAPTION"));
        Assert.That(run.Text.FontSize, Is.EqualTo(16));
        Assert.That(run.Effects.StrokeWidth, Is.EqualTo(2));
        Assert.That(run.Effects.Shadows.Span[0], Is.EqualTo(new TextShadow(1, 1, 2, new Rgba(0, 0, 0, 1))));
        Assert.That(Bounds(display, "label"), Is.EqualTo(new Rect(0, 0, 120, 20)));
        Assert.That(display.Runs.OfType<TextRun>(), Is.Empty, "effects and fill are one ordered native glyph primitive");
    }
    [Test]
    public void OrdinaryTextKeepsTheExistingTextRunFastPath()
    {
        var (_, input, display) = Fixture("<div id=label>CAPTION</div>", "#label{width:120px;white-space:nowrap}");
        using var owner = input;
        Assert.That(display.Runs.OfType<TextEffectsRun>(), Is.Empty);
        Assert.That(display.Runs.OfType<TextRun>().Single().Text, Is.EqualTo("CAPTION"));
    }
    [Test]
    public void DeferredBottomAnchorTranslatesGlyphEffectsAndFillTogether()
    {
        var (_, input, display) = Fixture("<div id=parent><div id=label>FISH</div></div>", """
            #parent{position:relative;width:100px;height:100px}
            #label{position:absolute;bottom:10px;width:80px;-webkit-text-stroke-width:1px;text-shadow:1px 1px 2px black}
            """);
        using var owner = input;
        var text = display.Runs.OfType<TextEffectsRun>().Single().Text;
        Assert.That(Bounds(display, "label").Y, Is.EqualTo(70));
        Assert.That(text.Baseline, Is.GreaterThanOrEqualTo(70).And.LessThan(90));
        Assert.That(text.X, Is.Zero);
    }
}
