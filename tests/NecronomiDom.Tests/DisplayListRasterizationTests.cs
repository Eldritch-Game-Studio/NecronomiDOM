using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests;

[TestFixture]
public sealed class DisplayListRasterizationTests
{
    // CSS Color 4 source-over alpha-zero identity.
    // This is conservative admission, not clipping/opacity visibility analysis or a replacement renderer.
    private static readonly Rect Bounds = new(0, 0, 1280, 720);

    [Test]
    public void EmptyAndEntirelyTransparentPlainBoxesDoNotRequireRasterization()
    {
        var display = new DisplayList();
        Assert.That(display.RequiresRasterization, Is.False);
        display.Runs.Add(new BoxRun(Bounds, new BoxStyle(new(1, 0, 0, 0), new(0, 1, 0, 0), 3, 8)));
        display.Runs.Add(new BoxRun(Bounds, new BoxStyle(default, new(1, 1, 1, 1), 0, 0)));
        Assert.That(display.RequiresRasterization, Is.False,
            "Transparent RGB and a colored zero-width border produce no source-over contribution.");
        Assert.That(display.Runs, Has.Count.EqualTo(2), "Admission must not rewrite the display list.");
    }

    [TestCase(1f, 0f, 0d)]
    [TestCase(float.Epsilon, 0f, 0d)]
    [TestCase(0f, 1f, 1d)]
    [TestCase(0f, float.Epsilon, 1d)]
    public void EveryNonzeroFillOrNonzeroWidthBorderRetainsRasterization(float fillAlpha, float borderAlpha, double borderWidth)
    {
        var display = new DisplayList();
        display.Runs.Add(new BoxRun(Bounds, default));
        display.Runs.Add(new BoxRun(Bounds, new BoxStyle(new(0, 0, 0, fillAlpha), new(0, 0, 0, borderAlpha), borderWidth, 0)));
        Assert.That(display.RequiresRasterization, Is.True, "No near-zero-alpha tolerance or first-run-only admission.");
    }

    [Test]
    public void LiveRunReplacementNeverReusesAnEarlierEmptyDecision()
    {
        var display = new DisplayList();
        var transparent = new BoxRun(Bounds, default);
        display.Runs.Add(transparent);
        Assert.That(display.RequiresRasterization, Is.False);
        display.Runs[0] = transparent with { Style = new BoxStyle(new(0, 0, 0, 1), default, 0, 0) };
        Assert.That(display.RequiresRasterization, Is.True);
        display.Runs[0] = transparent;
        Assert.That(display.RequiresRasterization, Is.False);
    }

    [Test]
    public void StateTextAndUnknownRunsConservativelyKeepBindingsAndAdapterValidation()
    {
        // DocumentPaint.Rebuild keeps its state/unsupported-run handling and transition bindings.
        // Even zero-opacity or currently transparent text is not proved inert by plain-box admission.
        var display = new DisplayList();
        foreach (PaintRun run in new PaintRun[]
        {
            new BeginOpacityRun(Bounds, 0), new EndOpacityRun(),
            new TextRun("", 0, 0, 16, default), new FuturePaintRun()
        })
        {
            display.Runs.Clear();
            display.Runs.Add(new BoxRun(Bounds, default));
            display.Runs.Add(run);
            Assert.That(display.RequiresRasterization, Is.True, run.GetType().Name);
        }
    }

    private sealed record FuturePaintRun : PaintRun;
}
