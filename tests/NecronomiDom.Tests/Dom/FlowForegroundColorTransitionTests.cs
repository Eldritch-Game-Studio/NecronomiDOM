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

namespace NecronomiDom.Tests.Dom;

public sealed class FlowForegroundColorTransitionTests
{
    private sealed class Metrics : ITextShaper
    {
        public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
            => new(text.Length * size * .5, size * .75, size * .25);
    }
    private static (Document Document, StyleEngine Styles, ForegroundColorTransitionController Color, FlowLayout Flow) Fixture(
        string css, string body, ForegroundColorTransitionProfile profile = ForegroundColorTransitionProfile.Css)
    {
        var document = HtmlImporter.Import("<!doctype html><style>html,body,div,input,textarea{display:block;margin:0;padding:0;" +
            "font-family:monospace;font-size:16px;line-height:20px}body{color:black}div{width:300px}" + css + "</style>" + body,
            "foreground-transitions", WebUrl.Parse("res:///foreground-transitions.html")!).Document;
        var styles = new StyleEngine(document); var color = new ForegroundColorTransitionController(profile);
        return (document, styles, color, new FlowLayout(styles, new Metrics(), "monospace", animatedForegroundColor: color));
    }
    private static TextRun Text(DisplayList display, string text) => display.Runs.Select(run => run switch
        { TextRun value => value, TextEffectsRun effects => effects.Text, _ => null }).Single(run => run?.Text == text)!;
    private static TextEffectsRun Effects(DisplayList display, string text) => display.Runs.OfType<TextEffectsRun>().Single(run => run.Text.Text == text);
    private static readonly Rgba Red = new(1, 0, 0, 1), Blue = new(0, 0, 1, 1), Purple = new(.5f, 0, .5f, 1);
    private const string Row = "#row{color:red;transition:color 1s linear}#row.changed{color:blue}";

    [TestCase("")]
    [TestCase("color:inherit")]
    [TestCase("color:unset")]
    [TestCase("color:currentcolor")]
    [TestCase("color:var(--missing)")]
    [TestCase("--ink:currentcolor;color:var(--ink)")]
    [TestCase("color:revert")]
    public void InheritedInkAnimatesButAnExplicitSameRgbChildDoesNot(string inherited)
    {
        var (document, styles, color, flow) = Fixture(Row + "#ink{" + inherited + "}#literal{color:red}",
            "<div id=row><div id=ink>inherited</div><div id=literal>literal</div></div>");
        var row = document.GetElementById("row")!; var child = document.GetElementById("ink")!;
        color.ReconcileStyles(document, styles, 0); var initial = flow.Layout(document, 400, 300);
        row.ClassList.Add("changed"); color.ReconcileStyles(document, styles, 0); color.Sample(.5);
        var interior = flow.Layout(document, 400, 300);
        Assert.That(Text(interior, "inherited").Color, Is.EqualTo(Purple));
        Assert.That(Text(interior, "literal").Color, Is.EqualTo(Red));
        Assert.That(styles.GetComputedValues(child).GetPropertyValue("color"), Is.EqualTo("rgb(0, 0, 255)"), "CSSOM remains the unmodified target");
        Assert.That(interior.Boxes.Select(box => box.Bounds), Is.EqualTo(initial.Boxes.Select(box => box.Bounds)), "Color must not alter logical metrics");
        color.Sample(1); var endpoint = flow.Layout(document, 400, 300);
        Assert.That(Text(endpoint, "inherited").Color, Is.EqualTo(Blue)); Assert.That(Text(endpoint, "literal").Color, Is.EqualTo(Red));
    }
    [TestCase("color:initial")]
    [TestCase("--ink:red;color:var(--ink)")]
    public void ExplicitInitialAndVariableLiteralDoNotAcquireAnAncestorAnimation(string css)
    {
        var (document, styles, color, flow) = Fixture(Row + "#ink{" + css + "}", "<div id=row><div id=ink>fixed</div></div>");
        color.ReconcileStyles(document, styles, 0); var initial = Text(flow.Layout(document, 400, 300), "fixed").Color;
        document.GetElementById("row")!.ClassList.Add("changed"); color.ReconcileStyles(document, styles, 0); color.Sample(.5);
        Assert.That(Text(flow.Layout(document, 400, 300), "fixed").Color, Is.EqualTo(initial));
    }
    [Test]
    public void RevertLayerUsesTheActualWinningInheritedDeclarationNotTheRevertedLiteral()
    {
        var (document, styles, color, flow) = Fixture("@layer base,top;" + Row +
            "@layer base{#ink{color:inherit}}@layer top{#ink{color:red;color:revert-layer}}", "<div id=row><div id=ink>layered</div></div>");
        color.ReconcileStyles(document, styles, 0); document.GetElementById("row")!.ClassList.Add("changed");
        color.ReconcileStyles(document, styles, 0); color.Sample(.5);
        Assert.That(Text(flow.Layout(document, 400, 300), "layered").Color, Is.EqualTo(Purple));
    }
    [Test]
    public void CurrentcolorExpressionUsesTheSampledParentWithoutChangingItsComputedTarget()
    {
        var (document, styles, color, flow) = Fixture(Row + "#ink{color:color-mix(in srgb,currentcolor 50%,black)}",
            "<div id=row><div id=ink>mixed</div></div>");
        color.ReconcileStyles(document, styles, 0); document.GetElementById("row")!.ClassList.Add("changed");
        color.ReconcileStyles(document, styles, 0); color.Sample(.5);
        Assert.That(Text(flow.Layout(document, 400, 300), "mixed").Color, Is.EqualTo(new Rgba(.25f, 0, .25f, 1)));
        Assert.That(ForegroundColorTransitionStyle.Resolve(styles.GetComputedValues(document.GetElementById("ink")!)).Color,
            Is.EqualTo(new Rgba(0, 0, .5f, 1)));
    }
    [Test]
    public void InheritedCurrentcolorEffectsTrackInkWhileLiteralAlphaUnitsAndOrderStayExact()
    {
        var (document, styles, color, flow) = Fixture(Row +
            "#row{font-size:20px;-webkit-text-stroke-width:.1em;text-shadow:.5em 1em .1em currentcolor,rgba(0,255,0,.4) -2px -3px 1px}" +
            "#ink,#literal{font-size:40px}#literal{-webkit-text-stroke-color:rgba(0,0,0,.25);text-shadow:rgba(0,0,0,.5) 1px 2px}",
            "<div id=row><div id=ink>dependent</div><div id=literal>fixed</div></div>");
        color.ReconcileStyles(document, styles, 0); document.GetElementById("row")!.ClassList.Add("changed");
        color.ReconcileStyles(document, styles, 0); color.Sample(.5); var display = flow.Layout(document, 400, 300);
        var animated = Effects(display, "dependent");
        Assert.That(animated.Text.Color, Is.EqualTo(Purple)); Assert.That(animated.Effects.StrokeColor, Is.EqualTo(Purple));
        Assert.That(animated.Effects.StrokeWidth, Is.EqualTo(2));
        Assert.That(animated.Effects.Shadows.Span[0], Is.EqualTo(new TextShadow(10, 20, 2, Purple)));
        Assert.That(animated.Effects.Shadows.Span[1], Is.EqualTo(new TextShadow(-2, -3, 1, new Rgba(0, 1, 0, .4f))));
        var literal = Effects(display, "fixed"); Assert.That(literal.Text.Color, Is.EqualTo(Purple));
        Assert.That(literal.Effects.StrokeColor, Is.EqualTo(new Rgba(0, 0, 0, .25f)));
        Assert.That(literal.Effects.Shadows.Span[0], Is.EqualTo(new TextShadow(1, 2, 0, new Rgba(0, 0, 0, .5f))));
        Assert.That(TextEffectsResolver.Resolve(styles.GetComputedValues(document.GetElementById("ink")!)).StrokeColor, Is.EqualTo(Blue),
            "The existing public effects resolver still reads ordinary computed targets");
    }
    [Test]
    public void NewlyEligibleChildStartsFromCurrentInheritedInkBeforeParentStyleCanSnap()
    {
        var (document, styles, color, flow) = Fixture(Row, "<div id=row><div id=ink>child</div></div>");
        var row = (HTMLElement)document.GetElementById("row")!; var child = (HTMLElement)document.GetElementById("ink")!;
        color.ReconcileStyles(document, styles, 0); row.ClassList.Add("changed"); color.ReconcileStyles(document, styles, 0);
        color.Sample(.5); Assert.That(Text(flow.Layout(document, 400, 300), "child").Color, Is.EqualTo(Purple));
        row.Style.CssText = "transition:none;color:lime";
        child.Style.CssText = "transition:color 1s linear;color:white";
        color.ReconcileStyles(document, styles, .5);
        Assert.That(Text(flow.Layout(document, 400, 300), "child").Color, Is.EqualTo(Purple), "Snapshot old inherited paint before updating the parent");
        color.Sample(1); Assert.That(Text(flow.Layout(document, 400, 300), "child").Color, Is.EqualTo(new Rgba(.75f, .5f, .75f, 1)));
    }
    [Test]
    public void MovingAnInheritedLabelRetargetsItsDependencyWithoutRgbEqualityGuessing()
    {
        var (document, styles, color, flow) = Fixture(Row + "#other{color:red}",
            "<div id=row><div id=ink>moving</div></div><div id=other></div>");
        color.ReconcileStyles(document, styles, 0); document.GetElementById("row")!.ClassList.Add("changed");
        color.ReconcileStyles(document, styles, 0); color.Sample(.5);
        Assert.That(Text(flow.Layout(document, 400, 300), "moving").Color, Is.EqualTo(Purple));
        document.GetElementById("other")!.AppendChild(document.GetElementById("ink")!); color.ReconcileStyles(document, styles, .5);
        Assert.That(Text(flow.Layout(document, 400, 300), "moving").Color, Is.EqualTo(Red));
    }
    [Test]
    public void SimplifiedProfileAnimatesBothInheritedLabelsAndFocusCascadeKeepsMetrics()
    {
        const string css = "body{--text-primary:rgb(240,232,210);--text-secondary:rgb(194,178,138);" +
            "--text-muted:rgb(140,128,102);--accent:rgb(214,170,88)}" +
            ".option-label{color:var(--text-secondary);transition-property:color,background-color;" +
            "transition-duration:.12s,.12s;transition-timing-function:ease-out,ease-out}" +
            ".option-label.option-fresh{color:var(--text-primary)}.option-label.option-seen{color:var(--text-muted)}" +
            ".option-label:hover{color:var(--text-primary)}.option-label:focus,.option-label.focused{color:var(--accent)}";
        var (document, styles, color, flow) = Fixture(css,
            "<div id=row class='option-label option-fresh'><div class=option-index>1.</div><div class=option-text>Choice</div></div>",
            ForegroundColorTransitionProfile.Simplified);
        var row = document.GetElementById("row")!;
        color.ReconcileStyles(document, styles, 0); var initial = flow.Layout(document, 400, 300);
        Assert.That(Text(initial, "Choice").Color, Is.EqualTo(new Rgba(240 / 255f, 232 / 255f, 210 / 255f, 1)));
        row.ClassList.Add("focused"); color.ReconcileStyles(document, styles, 0); color.Sample(.06);
        var interior = flow.Layout(document, 400, 300); Rgba ink = Text(interior, "Choice").Color;
        Assert.That(ink.R, Is.EqualTo(220.5 / 255).Within(1e-7)); Assert.That(ink.G, Is.EqualTo(185.5 / 255).Within(1e-7));
        Assert.That(ink.B, Is.EqualTo(118.5 / 255).Within(1e-7)); Assert.That(Text(interior, "1.").Color, Is.EqualTo(ink));
        Assert.That(interior.Boxes.Select(box => box.Bounds), Is.EqualTo(initial.Boxes.Select(box => box.Bounds)));
        row.ClassList.Remove("option-fresh"); row.ClassList.Add("option-seen"); color.ReconcileStyles(document, styles, .06);
        Assert.That(Text(flow.Layout(document, 400, 300), "Choice").Color, Is.EqualTo(ink), "Focused gold wins over seen ink");
        row.ClassList.Remove("focused"); color.ReconcileStyles(document, styles, .06); color.Sample(.180001);
        Assert.That(Text(flow.Layout(document, 400, 300), "Choice").Color, Is.EqualTo(new Rgba(140 / 255f, 128 / 255f, 102 / 255f, 1)));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void DisplayAndTreeWithdrawalCancelThenReentryStartsFresh(bool detach)
    {
        var (document, styles, color, flow) = Fixture(Row, "<div id=row><div>visible</div></div>");
        var row = (HTMLElement)document.GetElementById("row")!; var events = new List<TransitionEvent>();
        row.AddEventListener("transitioncancel", new DelegateEventListener(e => events.Add((TransitionEvent)e)));
        color.ReconcileStyles(document, styles, 0); row.ClassList.Add("changed"); color.ReconcileStyles(document, styles, 0);
        color.Sample(.25);
        if (detach) row.Remove(); else row.Style.SetProperty("display", "none");
        color.ReconcileStyles(document, styles, .25); color.DispatchEvents();
        Assert.That(color.Contains(row), Is.False); Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0].PropertyName, Is.EqualTo("color")); Assert.That(events[0].ElapsedTime, Is.EqualTo(.25));
        Assert.That(flow.Layout(document, 400, 300).Runs.OfType<TextRun>(), Is.Empty);
        if (detach) document.Body!.AppendChild(row); else row.Style.RemoveProperty("display");
        color.ReconcileStyles(document, styles, .3);
        Assert.That(color.HasRunningTransitions, Is.False); Assert.That(Text(flow.Layout(document, 400, 300), "visible").Color, Is.EqualTo(Blue));
        color.Clear(.3); color.ReconcileStyles(document, styles, .3); Assert.That(color.HasRunningTransitions, Is.False);
    }
    [Test]
    public void OrdinaryFlowStillRejectsMissingCapabilityAndOnlyPaintsStaticComputedTargets()
    {
        var (document, styles, color, _) = Fixture(Row, "<div id=row><div>ordinary</div></div>");
        var ordinary = new FlowLayout(styles, new Metrics(), "monospace");
        color.ReconcileStyles(document, styles, 0); document.GetElementById("row")!.ClassList.Add("changed");
        color.ReconcileStyles(document, styles, 0); color.Sample(.5);
        Assert.That(() => ordinary.Layout(document, 400, 300), Throws.TypeOf<NotSupportedException>().With.Message.Contains("transition"));
        ((HTMLElement)document.GetElementById("row")!).Style.SetProperty("transition", "initial");
        Assert.That(Text(ordinary.Layout(document, 400, 300), "ordinary").Color, Is.EqualTo(Blue));
        Assert.That(color.TryGetForegroundColor(document.GetElementById("row")!, out Rgba stillAnimated), Is.True);
        Assert.That(stillAnimated, Is.EqualTo(Purple), "The separate ordinary layout did not consume/mutate the controller");
    }
    [TestCase("input", "value='control'")]
    [TestCase("textarea", "")]
    public void ActualTextControlRunsUseSampledInkWithoutChangingTheirValueOrLogicalGeometry(string tag, string attributes)
    {
        var (document, styles, color, flow) = Fixture(Row +
            "input,textarea{appearance:none;resize:none;border:0;width:200px;height:40px;background-color:transparent;color:inherit}",
            "<div id=row><" + tag + " id=field " + attributes + ">" + (tag == "textarea" ? "control" : "") + "</" + tag + "></div>");
        color.ReconcileStyles(document, styles, 0); var initial = flow.Layout(document, 400, 300);
        document.GetElementById("row")!.ClassList.Add("changed"); color.ReconcileStyles(document, styles, 0); color.Sample(.5);
        var interior = flow.Layout(document, 400, 300); var text = Text(interior, "control");
        Assert.That(text.Color, Is.EqualTo(Purple)); Assert.That(text.TextControl, Is.SameAs(document.GetElementById("field")));
        Assert.That(text.ControlLine, Is.EqualTo(Text(initial, "control").ControlLine));
        Assert.That(((ITextControl)document.GetElementById("field")!).Value, Is.EqualTo("control"));
    }
    [Test]
    public void SlottedColorUsesTheAssignedSlotWhileUnsupportedShadowRenderingStaysRejected()
    {
        var (document, styles, color, flow) = Fixture("#host{color:lime}",
            "<div id=host><div id=ink>slotted</div></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var slot = (HTMLSlotElement)document.CreateElement("slot");
        slot.Style.CssText = "color:red;transition:color 1s linear";
        shadow.AppendChild(slot);
        var child = document.GetElementById("ink")!;
        Assert.That(child.AssignedSlot, Is.SameAs(slot));
        color.UpdateStyle(slot, styles.GetComputedValues(slot), 0);
        Assert.That(ForegroundColorTransitionStyle.Resolve(styles.GetComputedValues(child)).Color, Is.EqualTo(Red));
        slot.Style.SetProperty("color", "blue");
        color.UpdateStyle(slot, styles.GetComputedValues(slot), 0); color.Sample(.5);
        Assert.That(color.TryGetForegroundColor(slot, out Rgba sampledSlot), Is.True);
        var dependencies = styles.GetForegroundColorDependencies(child);
        Assert.That(ForegroundColorTransitionStyle.PaintColor(in dependencies, sampledSlot), Is.EqualTo(Purple));
        Assert.That(() => flow.Layout(document, 400, 300), Throws.TypeOf<NotSupportedException>().With.Message.Contains("shadow-root"));
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("color"), Is.EqualTo("rgb(0, 0, 255)"));
    }
    [Test]
    public void DeferredTrustedHandlersCanWithdrawTheRealRowAndReenterWithoutStaleInk()
    {
        var (document, styles, color, flow) = Fixture(Row, "<div id=row><div>callback</div></div>");
        var row = document.GetElementById("row")!; var events = new List<string>();
        row.AddEventListener("transitionrun", new DelegateEventListener(_ =>
        {
            events.Add("run");
            row.Remove(); color.ReconcileStyles(document, styles, .5);
            color.DispatchEvents(); // The existing reentrancy guard must not duplicate queued events.
        }));
        row.AddEventListener("transitioncancel", new DelegateEventListener(_ => events.Add("cancel")));
        color.ReconcileStyles(document, styles, 0); row.ClassList.Add("changed"); color.ReconcileStyles(document, styles, 0);
        color.Sample(.5); var painted = flow.Layout(document, 400, 300);
        Assert.That(Text(painted, "callback").Color, Is.EqualTo(Purple)); Assert.That(events, Is.Empty);
        color.DispatchEvents();
        Assert.That(events, Is.EqualTo(new[] { "run", "cancel" }));
        Assert.That(color.Contains(row), Is.False);
        Assert.That(flow.Layout(document, 400, 300).Runs.OfType<TextRun>(), Is.Empty);
        document.Body!.AppendChild(row); color.ReconcileStyles(document, styles, .5);
        Assert.That(color.HasRunningTransitions, Is.False);
        Assert.That(Text(flow.Layout(document, 400, 300), "callback").Color, Is.EqualTo(Blue));
    }
}
