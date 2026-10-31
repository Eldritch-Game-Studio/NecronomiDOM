using System;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

public sealed class FlowGeometryTransitionTests
{
    private sealed class Metrics : ITextShaper
    { public TextMetrics Measure(string text, int size, double fontWeight = 400, double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4); }
    private static Document Document(string css, string body)
        => HtmlImporter.Import("<!doctype html><style>html,body,div{display:block;margin:0;padding:0;" +
            "font-family:monospace;font-size:16px;line-height:20px}" + css + "</style>" + body,
            "geometry-transitions", WebUrl.Parse("res:///geometry-transitions.html")!).Document;
    private static LayoutBox Box(DisplayList list, Element element) => list.Boxes.Single(box => ReferenceEquals(box.Element, element));

    [TestCase("", "")]
    [TestCase("display:flex;flex-direction:row", "flex-shrink:0")]
    [TestCase("display:flex;flex-direction:column", "")]
    [TestCase("position:relative", "position:absolute;left:0;top:0")]
    public void PercentageSampleReachesBlockFlexAndPositionedLayoutBeforeChildAndPaintGeometry(string parentCss, string childCss)
    {
        var document = Document("#parent{width:240px;height:80px;" + parentCss + "}#fill{height:20px;width:0%;background-color:red;" +
            "transition:width .25s ease-out;" + childCss + "}#nested{width:50%;height:10px;background-color:blue}.filled{width:100%!important}",
            "<div id=parent><div id=fill><div id=nested></div></div></div>");
        var styles = new StyleEngine(document); var width = new WidthTransitionController(GeometryTransitionProfile.Simplified);
        var layout = new FlowLayout(styles, new Metrics(), "monospace", animatedWidth: width);
        var target = document.GetElementById("fill")!; var nested = document.GetElementById("nested")!;
        width.UpdateStyle(target, styles.GetComputedValues(target), 0); layout.Layout(document, 400, 200);
        target.ClassList.Add("filled"); width.UpdateStyle(target, styles.GetComputedValues(target), 0, 240); width.Sample(.125);
        var current = layout.Layout(document, 400, 200); var bounds = Box(current, target).Bounds;
        Assert.That(bounds.Width, Is.EqualTo(180)); Assert.That(Box(current, nested).Bounds.Width, Is.EqualTo(90));
        Assert.That(current.HitTest(bounds.X + 150, bounds.Y + 15), Is.SameAs(target));
        Assert.That(current.HitTest(bounds.X + 190, bounds.Y + 15), Is.Not.SameAs(target));
        Assert.That(current.Runs.OfType<BoxRun>().Any(run => run.Bounds == bounds && run.Style.Background.R == 1), Is.True);
        Assert.That(styles.GetComputedValues(target).GetPropertyValue("width"), Is.EqualTo("100%"));
        document.GetElementById("parent")!.SetAttribute("style", "width:320px");
        var resized = layout.Layout(document, 400, 200);
        Assert.That(Box(resized, target).Bounds.Width, Is.EqualTo(240));
        Assert.That(Box(resized, nested).Bounds.Width, Is.EqualTo(120));
    }
    [TestCase("content-box", 164)]
    [TestCase("border-box", 140)]
    public void SampledWidthPassesThroughExistingBoxSizingAndMinMaxConstraints(string sizing, double expected)
    {
        var document = Document("#target{width:0px;height:20px;padding:10px;border:2px solid red;min-width:80px;max-width:140px;" +
            "box-sizing:" + sizing + ";transition:width 1s linear}.filled{width:300px!important}", "<div id=target></div>");
        var styles = new StyleEngine(document); var width = new WidthTransitionController();
        var layout = new FlowLayout(styles, new Metrics(), "monospace", animatedWidth: width); var target = document.GetElementById("target")!;
        width.UpdateStyle(target, styles.GetComputedValues(target), 0); target.ClassList.Add("filled");
        width.UpdateStyle(target, styles.GetComputedValues(target), 0); width.Sample(.5);
        Assert.That(Box(layout.Layout(document, 400, 200), target).Bounds.Width, Is.EqualTo(expected));
    }
    [Test]
    public void ShrinkToFitIntrinsicWidthUsesCurrentPixelSampleNotTheAuthoredTarget()
    {
        var document = Document("#parent{position:absolute;top:0;left:0}#target{width:40px;height:20px;transition:width 1s linear}.filled{width:200px!important}",
            "<div id=parent><div id=target></div></div>");
        var styles = new StyleEngine(document); var width = new WidthTransitionController();
        var layout = new FlowLayout(styles, new Metrics(), "monospace", animatedWidth: width); var target = document.GetElementById("target")!;
        width.UpdateStyle(target, styles.GetComputedValues(target), 0); target.ClassList.Add("filled");
        width.UpdateStyle(target, styles.GetComputedValues(target), 0); width.Sample(.5);
        var current = layout.Layout(document, 400, 200);
        Assert.That(Box(current, target).Bounds.Width, Is.EqualTo(120));
        Assert.That(Box(current, document.GetElementById("parent")!).Bounds.Width, Is.EqualTo(120));
    }
    [Test]
    public void FlexAutomaticMinimumAndBasisUseSampleRatherThanFinalTarget()
    {
        var document = Document("#parent{display:flex;width:100px}#target{width:40px;transition:width 1s linear}.filled{width:200px!important}",
            "<div id=parent><div id=target>abcdefghijklmnopqrst</div></div>");
        var styles = new StyleEngine(document); var width = new WidthTransitionController();
        var layout = new FlowLayout(styles, new Metrics(), "monospace", animatedWidth: width); var target = document.GetElementById("target")!;
        width.UpdateStyle(target, styles.GetComputedValues(target), 0); target.ClassList.Add("filled");
        width.UpdateStyle(target, styles.GetComputedValues(target), 0); width.Sample(.25);
        Assert.That(Box(layout.Layout(document, 400, 200), target).Bounds.Width, Is.EqualTo(80));
    }
    [Test]
    public void ScaleSampleReplacesIndividualScaleOnceAndPaintHitGeometryAgreeWithoutResizingLayout()
    {
        var document = Document("#target{width:100px;height:40px;scale:1;translate:10px 0;transform:translateX(5px);" +
            "transform-origin:0 0;background-color:red;transition:scale 1s linear}.focused{scale:2!important}", "<div id=target></div>");
        var styles = new StyleEngine(document); var scale = new ScaleTransitionController();
        var layout = new FlowLayout(styles, new Metrics(), "monospace", animatedScale: scale); var target = document.GetElementById("target")!;
        scale.UpdateStyle(target, styles.GetComputedValues(target), 0); target.ClassList.Add("focused");
        scale.UpdateStyle(target, styles.GetComputedValues(target), 0); scale.Sample(.5);
        var current = layout.Layout(document, 400, 200); var bounds = Box(current, target).Bounds;
        Assert.That(bounds.Width, Is.EqualTo(100)); Assert.That(bounds.Height, Is.EqualTo(40));
        var transform = current.Runs.OfType<BeginTransformRun>().Single().Transform;
        var origin = transform.TransformPoint(bounds.X, bounds.Y); var edge = transform.TransformPoint(bounds.X + 100, bounds.Y + 40);
        Assert.That(origin.X, Is.EqualTo(bounds.X + 17.5)); Assert.That(edge.X - origin.X, Is.EqualTo(150));
        Assert.That(edge.Y - origin.Y, Is.EqualTo(60));
        Assert.That(current.HitTest(origin.X + 140, origin.Y + 50), Is.SameAs(target));
        Assert.That(current.HitTest(origin.X + 160, origin.Y + 50), Is.Not.SameAs(target));
        Assert.That(styles.GetComputedValues(target).GetPropertyValue("scale"), Is.EqualTo("2"));
    }
    [Test]
    public void ScaleToNoneRetiresTransformContainingBlockOnlyWhenAnimationCompletes()
    {
        var document = Document("#target{width:100px;height:40px;scale:2;transition:scale 1s linear}.left{scale:none!important}", "<div id=target></div>");
        var styles = new StyleEngine(document); var scale = new ScaleTransitionController();
        var layout = new FlowLayout(styles, new Metrics(), "monospace", animatedScale: scale); var target = document.GetElementById("target")!;
        scale.UpdateStyle(target, styles.GetComputedValues(target), 0); target.ClassList.Add("left");
        scale.UpdateStyle(target, styles.GetComputedValues(target), 0); scale.Sample(.5);
        Assert.That(layout.Layout(document, 400, 200).Runs.OfType<BeginTransformRun>().Count(), Is.EqualTo(1));
        scale.Sample(1); Assert.That(layout.Layout(document, 400, 200).Runs.OfType<BeginTransformRun>(), Is.Empty);
    }
    [Test]
    public void SampledScalePreservesOriginRotationAndRejectsHiddenThreeDimensionalTargets()
    {
        var document = Document("#target{width:80px;height:40px;rotate:90deg;scale:2 3;transform-origin:25% 75%}", "<div id=target></div>");
        var target = document.GetElementById("target")!; var style = new StyleEngine(document).GetComputedValues(target);
        var actual = BoxTransformResolver.Resolve(style, new(10, 20, 80, 40), new AnimatedScale(1.5, 2));
        var origin = actual.Matrix.TransformPoint(30, 50);
        Assert.That(origin.X, Is.EqualTo(30).Within(1e-12)); Assert.That(origin.Y, Is.EqualTo(50).Within(1e-12));
        var point = actual.Matrix.TransformPoint(32, 53);
        Assert.That(point.X, Is.EqualTo(24).Within(1e-12)); Assert.That(point.Y, Is.EqualTo(53).Within(1e-12));
        target.SetAttribute("style", "scale:2 3 4"); style = new StyleEngine(document).GetComputedValues(target);
        Assert.Throws<NotSupportedException>(() => BoxTransformResolver.Resolve(style, new(0, 0, 80, 40), AnimatedScale.Identity));
    }
}
