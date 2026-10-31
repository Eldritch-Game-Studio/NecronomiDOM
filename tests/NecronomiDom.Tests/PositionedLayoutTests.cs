using System;
using System.Globalization;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

[TestFixture]
public sealed class PositionedLayoutTests
{
    private static readonly PositionedContainingBlock Definite = new(new Rect(20, 30, 200, 100), new Rect(10, 20, 220, 120), true);
    private sealed class TextFreeShaper : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => throw new InvalidOperationException("The opacity probes contain no text.");
    }
    private static CSSStyleDeclaration Style(string css)
    {
        var d = HtmlImporter.Import("<!doctype html><div style='" + css + "'></div>", "positioned-test", WebUrl.Parse("res:///positioned-test.html")!).Document;
        return new StyleEngine(d).GetComputedStyle(d.QuerySelector("div")!);
    }
    private static PositionedBoxPlan Prepare(string css, PositionedContainingBlock? flow = null)
        => PositionedLayoutSolver.Prepare(Style(css), flow ?? Definite, Definite, 20, 30, new PositionedIntrinsicWidth(20, 60));
    private static void RectIs(Rect actual, Rect expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(1.0 / 64));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1.0 / 64));
        Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(1.0 / 64));
        Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(1.0 / 64));
    }
    [Test]
    public void RelativeOffsetsMovePaintButNotNormalFlowContribution()
    {
        var p = Prepare("position:relative;left:10%;top:25%;width:100px;height:40px;padding:5px;border:2px solid black");
        var r = PositionedLayoutSolver.Complete(p, 90);
        RectIs(r.BorderBox, new Rect(40, 55, 114, 54));
        Assert.That(r.FlowContribution, Is.EqualTo(54));
        Assert.That(r.EstablishesContainingBlock, Is.True);
        RectIs(r.PaddingBox, new Rect(42, 57, 110, 50));
    }
    [Test]
    public void AbsolutePercentagesUsePaddingBoxAndExcludeFlow()
    {
        var p = Prepare("position:absolute;left:10%;top:25%;width:50%;height:50%;padding:5px;border:2px solid black;box-sizing:border-box");
        var r = PositionedLayoutSolver.Complete(p, 500);
        RectIs(r.BorderBox, new Rect(32, 50, 110, 60));
        RectIs(r.ContentBox, new Rect(39, 57, 96, 46));
        Assert.That(r.FlowContribution, Is.Zero);
    }
    [Test]
    public void AbsoluteOpposingInsetsStretchAutoDimensions()
    {
        var r = PositionedLayoutSolver.Complete(Prepare("position:absolute;inset:10px 20px;padding:5px;border:2px solid black"), 500);
        RectIs(r.BorderBox, new Rect(30, 30, 180, 100));
        Assert.That(r.DescendantContainingBlock.HeightIsDefinite, Is.True);
    }
    [Test]
    public void DefiniteAndIndefinitePercentageHeightsDifferWithoutViewportSubstitution()
    {
        var definite = Prepare("height:50%;width:100px");
        var indefinite = Prepare("height:50%;width:100px", Definite with { HeightIsDefinite = false });
        Assert.That(definite.DefiniteContentHeight, Is.EqualTo(50));
        Assert.That(indefinite.DefiniteContentHeight, Is.Null);
        Assert.That(PositionedLayoutSolver.Complete(indefinite, 23).BorderBox.Height, Is.EqualTo(23));
        Assert.That(PositionedLayoutSolver.Complete(indefinite, 23).DescendantContainingBlock.HeightIsDefinite, Is.False);
    }
    [Test]
    public void RelativeIndefinitePercentageTopFallsBackToAutoAndBottom()
    {
        var r = PositionedLayoutSolver.Complete(Prepare("position:relative;top:50%;bottom:7px;height:20px", Definite with { HeightIsDefinite = false }), 0);
        Assert.That(r.BorderBox.Y, Is.EqualTo(23));
    }
    [Test]
    public void AbsoluteBottomAutoHeightFinalizesAfterNaturalContent()
    {
        var p = Prepare("position:absolute;right:10px;bottom:5px;width:40px;padding:3px");
        var r = PositionedLayoutSolver.Complete(p, 30);
        RectIs(r.BorderBox, new Rect(174, 99, 46, 36));
        Assert.That(r.BorderBox.Y, Is.Not.EqualTo(p.PreLayoutBorderBoxOriginY));
    }
    [Test]
    public void AutoAbsoluteWidthUsesRealIntrinsicShrinkToFit()
    {
        var r = PositionedLayoutSolver.Complete(Prepare("position:absolute;left:5px;height:20px"), 0);
        RectIs(r.BorderBox, new Rect(15, 30, 60, 20));
    }
    [Test]
    public void AbsoluteAutoMarginsSolveConstraintNotNormalFlowCentering()
    {
        var r = PositionedLayoutSolver.Complete(Prepare("position:absolute;inset:10px;width:100px;height:40px;margin:auto"), 0);
        RectIs(r.BorderBox, new Rect(70, 60, 100, 40));
    }
    [Test]
    public void StaticIgnoresInsetsAndPreservesHorizontalAutoMargins()
    {
        var r = PositionedLayoutSolver.Complete(Prepare("left:70px;top:90px;width:100px;height:30px;margin-left:auto;margin-right:auto"), 0);
        RectIs(r.BorderBox, new Rect(70, 30, 100, 30));
        Assert.That(r.EstablishesContainingBlock, Is.False);
    }
    [Test]
    public void PaintContextCarriesPaddingClipAndGroupOpacityWithoutMultiplyingRunAlpha()
    {
        var s = Style("position:relative;width:80px;height:60px;padding:5px;border:2px solid black;overflow:hidden;opacity:0.5");
        var p = PositionedLayoutSolver.Prepare(s, Definite, Definite, 20, 30, new PositionedIntrinsicWidth(0, 0));
        var r = PositionedLayoutSolver.Complete(p, 0);
        var c = PositionedLayoutSolver.ResolvePaintContext(s, r);
        Assert.That(c.ClipsX && c.ClipsY, Is.True);
        RectIs(c.ClipBounds, r.PaddingBox);
        Assert.That(c.GroupOpacity, Is.EqualTo(0.5));
    }
    [TestCase(512, 768, 10, 1, 9)]
    [TestCase(20, 10, 100, 2, 5)]
    [TestCase(20, 10, 1, 2, 0)]
    public void RoundedOverflowResolvesCssOuterOverlapThenInnerPaddingRadius(double width, double height, double radius, double border, double expected)
    {
        var s = Style(FormattableString.Invariant($"width:{width}px;height:{height}px;border:{border}px solid black;border-radius:{radius}px;overflow:hidden"));
        var r = PositionedLayoutSolver.Complete(PositionedLayoutSolver.Prepare(s, Definite, Definite, 20, 30, new PositionedIntrinsicWidth(0, 0)), 0);
        Assert.That(PositionedLayoutSolver.ResolvePaintContext(s, r).ClipRadius, Is.EqualTo(expected));
    }
    [Test]
    public void NoncollapsingFlowMarginsUseContainingWidthAndPreserveBodyArithmetic()
    {
        var p = Prepare("width:100px;height:30px;margin-top:10%;margin-bottom:8px;position:relative;top:5px");
        var r = PositionedLayoutSolver.Complete(p, 0);
        Assert.That(p.MarginTop, Is.EqualTo(20));
        RectIs(r.BorderBox, new Rect(20, 55, 100, 30));
        Assert.That(r.FlowContribution, Is.EqualTo(58));
    }
    [Test]
    public void ReplacedAutoBoxKeepsIntrinsicSizeDespiteOpposingAbsoluteInsets()
    {
        var image = new ImageResource("intrinsic-40x20", 40, 20);
        var p = PositionedLayoutSolver.Prepare(Style("position:absolute;inset:5px 10px"), Definite, Definite, 20, 30,
            new PositionedIntrinsicWidth(40, 40), image);
        RectIs(PositionedLayoutSolver.Complete(p, 500).BorderBox, new Rect(20, 25, 40, 20));
    }
    [TestCase(true, 100, 50)]
    [TestCase(false, 40, 20)]
    public void ReplacedPercentageHeightUsesDefiniteBasisOrIntrinsicAspect(bool definite, double width, double height)
    {
        var image = new ImageResource("intrinsic-40x20", 40, 20);
        var p = PositionedLayoutSolver.Prepare(Style("height:50%"), Definite with { HeightIsDefinite = definite },
            Definite, 20, 30, new PositionedIntrinsicWidth(40, 40), image);
        RectIs(PositionedLayoutSolver.Complete(p, 500).BorderBox, new Rect(20, 30, width, height));
    }
    [Test]
    public void ReplacedAutoNormalWidthDoesNotStretchAndAutoMarginsCenterActualUsedWidth()
    {
        var p = PositionedLayoutSolver.Prepare(Style("margin-left:auto;margin-right:auto"), Definite, Definite, 20, 30,
            new PositionedIntrinsicWidth(40, 40), new ImageResource("intrinsic-40x20", 40, 20));
        RectIs(PositionedLayoutSolver.Complete(p, 500).BorderBox, new Rect(100, 30, 40, 20));
    }
    [Test]
    public void WidthConstraintsApplyBeforeAutoMarginAndAbsoluteAxisPlacement()
    {
        var centered = PositionedLayoutSolver.Complete(Prepare("width:300px;min-width:120px;max-width:100px;height:20px;margin-left:auto;margin-right:auto"), 0);
        RectIs(centered.BorderBox, new Rect(60, 30, 120, 20));
        var absolute = PositionedLayoutSolver.Complete(Prepare("position:absolute;right:10px;width:200px;max-width:60px;height:20px"), 0);
        RectIs(absolute.BorderBox, new Rect(160, 30, 60, 20));
    }
    [Test]
    public void NaturalHeightConstraintsApplyBeforeBottomPositionButDoNotInventDefiniteness()
    {
        var p = Prepare("position:absolute;bottom:10px;width:40px;min-height:50px;max-height:20px");
        RectIs(PositionedLayoutSolver.Complete(p, 5).BorderBox, new Rect(20, 80, 40, 50));
        var natural = PositionedLayoutSolver.Complete(Prepare("min-height:60px;max-height:90px"), 200);
        Assert.That(natural.ContentBox.Height, Is.EqualTo(90));
        Assert.That(natural.DescendantContainingBlock.HeightIsDefinite, Is.False);
    }
    [Test]
    public void DeclaredHeightConstraintsResolveBeforeChildPercentageBasis()
    {
        var p = Prepare("height:40px;min-height:70px;max-height:60px");
        Assert.That(p.DefiniteContentHeight, Is.EqualTo(70));
        var r = PositionedLayoutSolver.Complete(p, 0);
        Assert.That(r.DescendantContainingBlock.HeightIsDefinite, Is.True);
        Assert.That(r.ContentBox.Height, Is.EqualTo(70));
    }
    [Test]
    public void BorderBoxConstraintsConvertToContentLimits()
    {
        var p = Prepare("width:200px;max-width:80px;height:100px;max-height:60px;padding:5px;border:2px solid black;box-sizing:border-box");
        Assert.That(p.ContentWidth, Is.EqualTo(66));
        Assert.That(p.DefiniteContentHeight, Is.EqualTo(46));
        RectIs(PositionedLayoutSolver.Complete(p, 0).BorderBox, new Rect(20, 30, 80, 60));
    }
    [Test]
    public void IndefinitePercentageHeightLimitsBecomeZeroAndNoneNotViewportLengths()
    {
        var flow = Definite with { HeightIsDefinite = false };
        var p = Prepare("min-height:80%;max-height:20%", flow);
        Assert.That(p.MinContentHeight, Is.Zero);
        Assert.That(p.MaxContentHeight, Is.EqualTo(double.PositiveInfinity));
        Assert.That(PositionedLayoutSolver.Complete(p, 35).ContentBox.Height, Is.EqualTo(35));
        Assert.That(PositionedLayoutSolver.Complete(Prepare("min-height:80%;max-height:20%"), 35).ContentBox.Height, Is.EqualTo(80));
    }
    [TestCase(false, 12)]
    [TestCase(true, 90)]
    public void FinalUsedCrossHeightIsIndependentOfDescendantPercentageDefiniteness(bool definite, double childHeight)
    {
        var plan = Prepare("height:auto") with { DefiniteContentHeight = 90, DescendantHeightIsDefinite = definite };
        var container = PositionedLayoutSolver.Complete(plan, 12);
        Assert.That(container.ContentBox.Height, Is.EqualTo(90));
        Assert.That(container.DescendantContainingBlock.HeightIsDefinite, Is.EqualTo(definite));
        var child = PositionedLayoutSolver.Prepare(Style("height:100%"), container.DescendantContainingBlock, Definite,
            container.ContentBox.X, container.ContentBox.Y, new PositionedIntrinsicWidth(0, 0));
        Assert.That(PositionedLayoutSolver.Complete(child, 12).ContentBox.Height, Is.EqualTo(childHeight));
    }
    [Test]
    public void PreciseNumberMutationNotifiesDespiteIdenticalCssomTextAndRetainsEqualValueNoop()
    {
        double first = 127d / 255d, second = first + 0.0000001;
        var declaration = CSSStyleDeclaration.Parse("opacity:" + first.ToString("R", CultureInfo.InvariantCulture));
        string text = declaration.CssText;
        int changes = 0;
        declaration.Changed = () => changes++;
        declaration.SetProperty("opacity", second.ToString("R", CultureInfo.InvariantCulture));
        Assert.That(declaration.CssText, Is.EqualTo(text));
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(((CssNumberValue)declaration.EnumerateLonghands().Single().Value).Value, Is.EqualTo(second));
        declaration.SetProperty("opacity", second.ToString("R", CultureInfo.InvariantCulture));
        Assert.That(changes, Is.EqualTo(1));
    }
    [Test]
    public void NumericMutationRetainsIntegerIdentityAlongsideEqualValueNoop()
    {
        var declaration = CSSStyleDeclaration.Parse("opacity:1");
        int changes = 0;
        declaration.Changed = () => changes++;
        declaration.SetProperty("opacity", "1.0");
        Assert.That(declaration.GetPropertyValue("opacity"), Is.EqualTo("1"));
        Assert.That(((CssNumberValue)declaration.EnumerateLonghands().Single().Value).IsInteger, Is.False);
        Assert.That(changes, Is.EqualTo(1));
        declaration.SetProperty("opacity", "1.0");
        Assert.That(changes, Is.EqualTo(1));
    }
    [Test]
    public void UsedOpacityRetainsComputedPrecisionWithoutChangingCssomSerialization()
    {
        double alpha = 127d / 255d;
        var style = Style("opacity:" + alpha.ToString("R", CultureInfo.InvariantCulture));
        var box = PositionedLayoutSolver.Complete(PositionedLayoutSolver.Prepare(style, Definite, Definite, 20, 30,
            new PositionedIntrinsicWidth(0, 0)), 0);
        Assert.That(style.GetPropertyValue("opacity"), Is.EqualTo("0.498039"));
        Assert.That(PositionedLayoutSolver.ResolvePaintContext(style, box).GroupOpacity, Is.EqualTo(alpha));
    }
    [Test]
    public void LiveOpacityUpdatesWithIdenticalCssomTextReachSingleFloatCastPaintRuns()
    {
        var document = HtmlImporter.Import("<!doctype html><div id='alpha' style='position:relative;width:40px;height:20px'></div>",
            "opacity-precision", WebUrl.Parse("res:///opacity-precision.html")!).Document;
        var element = (HTMLElement)document.GetElementById("alpha")!;
        var styles = new StyleEngine(document);
        var computed = styles.GetComputedStyle(element);
        foreach (double alpha in new[] { 127d / 255d, 127d / 255d + 0.0000001, 0.9999999, 0.99999999 })
        {
            element.Style.SetProperty("opacity", alpha.ToString("R", CultureInfo.InvariantCulture));
            Assert.That(computed.GetPropertyValue("opacity"), Is.EqualTo(alpha < 0.9 ? "0.498039" : "1"));
            var display = new FlowLayout(styles, new TextFreeShaper(), "serif").Layout(document, 200, 100);
            var run = display.Runs.OfType<BeginOpacityRun>().Single();
            Assert.That(BitConverter.SingleToInt32Bits(run.Opacity), Is.EqualTo(BitConverter.SingleToInt32Bits((float)alpha)));
        }
    }
    [TestCase("position:fixed")]
    [TestCase("position:sticky")]
    [TestCase("position:absolute;z-index:2")]
    [TestCase("direction:rtl")]
    public void UnsupportedModesAreExplicit(string css) => Assert.Throws<NotSupportedException>(() => Prepare(css));
    [TestCase("overflow:auto")]
    [TestCase("overflow:scroll")]
    public void ScrollingUsesTheExistingPaddingEdgeClip(string css)
    {
        var s = Style(css);
        var r = PositionedLayoutSolver.Complete(PositionedLayoutSolver.Prepare(s, Definite, Definite, 20, 30, new PositionedIntrinsicWidth(0, 0)), 0);
        var context = PositionedLayoutSolver.ResolvePaintContext(s, r);
        Assert.That(context.ClipsX && context.ClipsY, Is.True);
        Assert.That(context.ClipBounds, Is.EqualTo(r.PaddingBox));
    }
}

