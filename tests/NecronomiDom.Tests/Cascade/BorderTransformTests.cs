using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

public sealed class BorderTransformTests
{
    private static CSSStyleDeclaration Computed(string css)
    {
        var document = new Document().Implementation.CreateHTMLDocument("border-transform");
        var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element);
        element.Style.CssText = css;
        return document.DefaultView.StyleEngine.GetComputedValues(element);
    }
    [Test]
    public void IndependentEdgesAndCornerOverlapRemainIndependent()
    {
        var run = BoxDecorationResolver.Resolve(Computed("background-color:rgba(1,2,3,.25);border-style:solid;border-width:3px 2px 7px 5px;border-color:red transparent blue green;border-radius:80px 40px 20px 10px / 30px 50px 40px 20px"), new(10,20,60,70));
        Assert.That(run.Borders.Top.Width, Is.EqualTo(3));
        Assert.That(run.Borders.Right.Width, Is.EqualTo(2));
        Assert.That(run.Borders.Right.Color.A, Is.Zero);
        Assert.That(run.Borders.Bottom.Width, Is.EqualTo(7));
        Assert.That(run.Borders.Left.Width, Is.EqualTo(5));
        Assert.That(run.Background.A, Is.EqualTo(.25f));
        Assert.That(run.Radii.TopLeft, Is.EqualTo(new BorderCorner(40,15)));
        Assert.That(run.Radii.TopRight, Is.EqualTo(new BorderCorner(20,25)));
        Assert.That(run.Radii.BottomRight, Is.EqualTo(new BorderCorner(10,20)));
        Assert.That(run.Radii.BottomLeft, Is.EqualTo(new BorderCorner(5,10)));
    }
    [Test]
    public void TransparentTriangleSidesKeepUsedWidthsAndNoneDoesNotPaint()
    {
        var triangle = BoxDecorationResolver.Resolve(Computed("border-style:solid;border-width:0 8px 12px 8px;border-color:transparent transparent red transparent"), new(0,0,16,12));
        Assert.That(triangle.Borders.Left.Width, Is.EqualTo(8));
        Assert.That(triangle.Borders.Left.Color.A, Is.Zero);
        Assert.That(triangle.Borders.Bottom.Color.A, Is.EqualTo(1));
        var none = BoxDecorationResolver.Resolve(Computed("border-width:20px;border-style:none"), new(0,0,30,30));
        Assert.That(none.Borders.Top.Width, Is.Zero);
        Assert.Throws<NotSupportedException>(() => BoxDecorationResolver.Resolve(Computed("border:3px dashed red"), new(0,0,30,30)));
    }
    [TestCase(-24)]
    [TestCase(24)]
    [TestCase(-7)]
    [TestCase(12)]
    public void AuthoredSignedRotationsPreserveOriginAndInverse(double degrees)
    {
        var used = BoxTransformResolver.Resolve(Computed($"rotate:{degrees}deg;transform-origin:20% 80%"), new(100,40,50,30));
        var origin = used.Matrix.TransformPoint(110,64);
        Assert.That(origin.X, Is.EqualTo(110).Within(1e-10)); Assert.That(origin.Y, Is.EqualTo(64).Within(1e-10));
        var p = used.Matrix.TransformPoint(125,55);
        Assert.That(used.Matrix.TryInversePoint(p.X,p.Y,out double x,out double y), Is.True);
        Assert.That(x, Is.EqualTo(125).Within(1e-10)); Assert.That(y, Is.EqualTo(55).Within(1e-10));
    }
    [Test]
    public void FishRollReflectionAndIndividualChannelsComposeInCssOrder()
    {
        var used = BoxTransformResolver.Resolve(Computed("translate:10px 20px;rotate:90deg;scale:1 -1;transform:translateX(5px);transform-origin:0 0"), new(100,40,50,30));
        var p = used.Matrix.TransformPoint(102,43);
        Assert.That(p.X, Is.EqualTo(113).Within(1e-10)); Assert.That(p.Y, Is.EqualTo(67).Within(1e-10));
        Assert.That(used.EstablishesContainingBlock, Is.True);
    }
    [Test]
    public void PercentTranslateAndOriginUseFinalBoundsAfterResize()
    {
        var style = Computed("width:50px;height:56px;translate:-50% -50%;scale:1.6 1.6;transform-origin:50% 50%");
        var small = BoxTransformResolver.Resolve(style, new(20,30,40,60));
        var large = BoxTransformResolver.Resolve(style, new(20,30,80,100));
        Assert.That(small.Matrix.TransformPoint(40,60), Is.EqualTo((20d,30d)));
        Assert.That(large.Matrix.TransformPoint(60,80), Is.EqualTo((20d,30d)));
    }
    [Test]
    public void PercentageTransformListAndGenuineMatrixUseTheSameCssConvention()
    {
        var list = BoxTransformResolver.Resolve(Computed("width:90px;height:80px;transform:translate(50%,25%) rotate(90deg) scale(2,-1);transform-origin:0 0"), new(10,20,40,60));
        var point = list.Matrix.TransformPoint(12,23);
        Assert.That(point.X, Is.EqualTo(33).Within(1e-10));
        Assert.That(point.Y, Is.EqualTo(39).Within(1e-10));
        var matrix = BoxTransformResolver.Resolve(Computed("transform:matrix(1,2,3,4,5,6);transform-origin:0 0"), new(10,20,40,60));
        Assert.That(matrix.Matrix.TransformPoint(12,23), Is.EqualTo((26d,42d)));
    }
    [Test]
    public void NumericalIdentityStillEstablishesContainingBlockButDegenerateNeverInverts()
    {
        Assert.That(BoxTransformResolver.Resolve(Computed("transform:none"), new()).EstablishesContainingBlock, Is.False);
        var identity = BoxTransformResolver.Resolve(Computed("scale:1"), new(0,0,30,30));
        Assert.That(identity.Matrix, Is.EqualTo(Affine2D.Identity)); Assert.That(identity.EstablishesContainingBlock, Is.True);
        var singular = BoxTransformResolver.Resolve(Computed("scale:0 1"), new(10,20,30,30));
        Assert.That(singular.EstablishesContainingBlock, Is.True);
        Assert.That(singular.Matrix.TryInversePoint(25,35,out double x,out double y), Is.False);
        Assert.That(double.IsNaN(x) && double.IsNaN(y), Is.True);
        Assert.That(singular.Matrix.TransformBounds(new(10,20,30,30)).Width, Is.Zero);
    }
    [Test]
    public void NestedViewportAffinesComposeExactlyOnce()
    {
        var parent = BoxTransformResolver.Resolve(Computed("rotate:30deg;transform-origin:25% 70%"), new(20,30,120,80)).Matrix;
        var child = BoxTransformResolver.Resolve(Computed("scale:1 -1;translate:4px -8px;transform-origin:0 100%"), new(40,45,30,20)).Matrix;
        var local = child.TransformPoint(46,51);
        var actual = Affine2D.Compose(parent,child).TransformPoint(46,51);
        var expected = parent.TransformPoint(local.X,local.Y);
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-10));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-10));
    }
}
