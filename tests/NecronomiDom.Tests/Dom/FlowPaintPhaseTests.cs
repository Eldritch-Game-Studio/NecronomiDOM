using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed partial class FlowPositionedInputTests
{
    private static readonly Rgba YellowFill = new(1, 192f / 255, 0, 1), BlueFill = new(32f / 255, 96f / 255, 192f / 255, 1);
    private static int FillIndex(DisplayList display, string id, Rgba color) => display.Runs.FindIndex(run =>
        run is BoxRun box && box.Bounds == Bounds(display, id) && box.Style.Background == color);

    [Test]
    public void EarlierRelativePaintsAndHitsAboveLaterOpaqueNormalFlowSibling()
    {
        var (doc, input, display) = Fixture("<div id=scope><div id=raised></div><div id=later></div></div>",
            "#scope{position:absolute;width:100px;height:50px}" +
            "#raised{position:relative;top:20px;width:50px;height:30px;background:#ffc000}" +
            "#later{width:50px;height:20px;background:#2060c0}");
        using var owner = input;
        Assert.That(FillIndex(display, "raised", YellowFill), Is.GreaterThan(FillIndex(display, "later", BlueFill)));
        Assert.That(Click(doc, input, 10, 35), Is.EqualTo("raised"));
    }
    [Test]
    public void PositionedDescendantPassesStaticAncestorsWithoutLosingTheirOverflowScope()
    {
        var (doc, input, display) = Fixture("<div id=ancestor><div id=raised></div></div><div id=later></div>",
            "#ancestor{width:30px;height:20px;overflow:hidden}" +
            "#raised{position:relative;top:5px;width:40px;height:30px;background:#ffc000}" +
            "#later{width:40px;height:30px;background:#2060c0}");
        using var owner = input;
        int raised = FillIndex(display, "raised", YellowFill);
        Assert.That(raised, Is.GreaterThan(FillIndex(display, "later", BlueFill)));
        Assert.That(display.Runs[raised - 1], Is.TypeOf<BeginRectClipRun>());
        Assert.That(((BeginRectClipRun)display.Runs[raised - 1]).Bounds, Is.EqualTo(Bounds(display, "ancestor")));
        Assert.That(display.Runs[raised + 1], Is.TypeOf<EndRectClipRun>());
        Assert.That(Click(doc, input, 10, 10), Is.EqualTo("raised"));
        Assert.That(Click(doc, input, 10, 25), Is.EqualTo("later"));
    }
    [Test]
    public void StaticOpacityContextSharesPositionedPhaseButKeepsItsChildrenAtomic()
    {
        var (doc, input, display) = Fixture("<div id=raised></div><div id=group><div id=inner></div></div>",
            "#raised{position:relative;top:60px;width:100px;height:60px;background:#ffc000}" +
            "#group{opacity:.5;width:100px;height:60px}" +
            "#inner{width:100px;height:60px;background:#2060c0}");
        using var owner = input;
        int raised = FillIndex(display, "raised", YellowFill), inner = FillIndex(display, "inner", BlueFill);
        Assert.That(inner, Is.GreaterThan(raised));
        Assert.That(display.Runs[inner - 1], Is.TypeOf<BeginOpacityRun>());
        Assert.That(display.Runs[inner + 1], Is.TypeOf<EndOpacityRun>());
        Assert.That(Click(doc, input, 10, 70), Is.EqualTo("inner"));
    }
    [Test]
    public void DeferredAbsoluteAndRelativeFragmentsUseDomOrderWithinTheirScopedPhase()
    {
        var (doc, input, display) = Fixture("<div id=absolute></div><div id=relative></div>",
            "#absolute{position:absolute;left:0;top:0;width:40px;height:40px;background:#2060c0}" +
            "#relative{position:relative;width:40px;height:40px;background:#ffc000}");
        using var owner = input;
        Assert.That(FillIndex(display, "relative", YellowFill), Is.GreaterThan(FillIndex(display, "absolute", BlueFill)));
        Assert.That(Click(doc, input, 10, 10), Is.EqualTo("relative"));
    }
    [Test]
    public void BottomAnchoredAutoHeightTranslatesHeldRunsAndTheirInheritedClipTogether()
    {
        var (doc, input, display) = Fixture("<div id=anchor><div id=clip><div id=raised></div></div></div>",
            "#anchor{position:absolute;bottom:10px;width:100px}" +
            "#clip{width:30px;height:20px;overflow:hidden}" +
            "#raised{position:relative;top:5px;width:40px;height:30px;background:#ffc000}");
        using var owner = input;
        int raised = FillIndex(display, "raised", YellowFill);
        Assert.That(Bounds(display, "raised"), Is.EqualTo(new Rect(0, 275, 40, 30)));
        Assert.That(((BeginRectClipRun)display.Runs[raised - 1]).Bounds, Is.EqualTo(Bounds(display, "clip")));
        Assert.That(Click(doc, input, 10, 280), Is.EqualTo("raised"));
        Assert.That(Click(doc, input, 10, 295), Is.Empty);
    }
}
