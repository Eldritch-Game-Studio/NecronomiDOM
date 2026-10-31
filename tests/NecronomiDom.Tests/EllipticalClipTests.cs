using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests;

public sealed class EllipticalClipTests
{
    private static BorderSides Sides(double top,double right,double bottom,double left) => new(
        new(top,default,"solid"),new(right,default,"solid"),new(bottom,default,"solid"),new(left,default,"solid"));
    [Test]
    public void PaddingEdgeSubtractsActualDifferentBorderInsetsNotContentPadding()
    {
        var outer = new BorderRadii(new(30,20),new(40,25),new(22,18),new(15,35));
        var clip = EllipticalClipGeometry.FromBorderBox(new(10,20,160,100),Sides(3,7,11,5),outer);
        Assert.That(clip.Bounds,Is.EqualTo(new Rect(15,23,148,86)));
        Assert.That(clip.Radii,Is.EqualTo(new BorderRadii(new(25,17),new(33,22),new(15,7),new(10,24))));
        Assert.That(clip.ContainsPoint(16,24),Is.False);
        Assert.That(clip.ContainsPoint(80,60),Is.True);
        Assert.That(clip.ContainsPoint(14,60),Is.False);
    }
    [Test]
    public void CommonOuterOverlapFactorPrecedesInnerInsetAndFinalInnerOverlap()
    {
        var clip = EllipticalClipGeometry.FromBorderBox(new(0,0,100,60),Sides(3,7,11,5),
            new(new(80,50),new(40,30),new(60,40),new(20,20)));
        double outerFactor = 100d/120;
        Assert.That(clip.Radii.TopLeft.X,Is.EqualTo(80*outerFactor-5).Within(1e-10));
        Assert.That(clip.Radii.TopRight.Y,Is.EqualTo(30*outerFactor-3).Within(1e-10));
        Assert.That(clip.Bounds,Is.EqualTo(new Rect(5,3,88,46)));
        var tight = EllipticalClipGeometry.FromBorderBox(new(0,0,100,100),Sides(0,0,0,20),
            new(new(10,10),new(90,90),new(90,10),new(10,10)));
        Assert.That(tight.Radii.TopRight.X,Is.EqualTo(80));
        Assert.That(tight.Radii.TopLeft.X,Is.Zero);
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void EachIndependentEllipseUsesItsOwnTwoAxes(int corner)
    {
        var geometry = new EllipticalClipGeometry(new(10,20,120,100),new(new(30,20),new(20,35),new(40,15),new(10,25)));
        BorderCorner radius = corner switch {0=>geometry.Radii.TopLeft,1=>geometry.Radii.TopRight,2=>geometry.Radii.BottomRight,_=>geometry.Radii.BottomLeft};
        double originX = corner is 0 or 3 ? 10 : 130, originY = corner < 2 ? 20 : 120;
        double sx = corner is 0 or 3 ? 1 : -1, sy = corner < 2 ? 1 : -1;
        Assert.That(geometry.ContainsPoint(originX+sx*radius.X*.1,originY+sy*radius.Y*.1),Is.False);
        Assert.That(geometry.ContainsPoint(originX+sx*radius.X*.5,originY+sy*radius.Y*.5),Is.True);
        Assert.That(geometry.ContainsPoint(originX+sx*radius.X,originY+sy*.001),Is.True);
    }
    [Test]
    public void EitherZeroAxisMakesCornerSquareAndCollapsedClipIsNeverHit()
    {
        var square = new EllipticalClipGeometry(new(0,0,40,40),new(new(0,20),new(20,0),default,default));
        Assert.That(square.ContainsPoint(0,0),Is.True); Assert.That(square.ContainsPoint(39,0),Is.True);
        Assert.That(new EllipticalClipGeometry(new(1,2,0,40),default).ContainsPoint(1,20),Is.False);
        Assert.That(square.ContainsPoint(double.NaN,1),Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new EllipticalClipGeometry(new(0,0,40,40),new(new(-1,2),default,default,default)));
    }
    [Test]
    public void TrueComputedPercentageRadiiResolveAgainstFinalResizedBorderBounds()
    {
        var document = new Document().Implementation.CreateHTMLDocument("elliptical-clip");
        var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element);
        element.Style.CssText="width:80px;height:40px;border-style:solid;border-width:3px 7px 11px 5px;border-radius:20% 30% 15% 10% / 40% 20% 35% 15%;padding:27px";
        CSSStyleDeclaration computed = document.DefaultView.StyleEngine.GetComputedValues(element);
        var small = BoxDecorationResolver.Resolve(computed,new(10,20,100,80));
        var large = BoxDecorationResolver.Resolve(computed,new(10,20,200,120));
        var a = EllipticalClipGeometry.FromBorderBox(small.Bounds,small.Borders,small.Radii);
        var b = EllipticalClipGeometry.FromBorderBox(large.Bounds,large.Borders,large.Radii);
        Assert.That(a.Radii.TopLeft,Is.EqualTo(new BorderCorner(15,29)));
        Assert.That(b.Radii.TopLeft,Is.EqualTo(new BorderCorner(35,45)));
        Assert.That(a.Bounds,Is.EqualTo(new Rect(15,23,88,66)));
        Assert.That(b.Bounds,Is.EqualTo(new Rect(15,23,188,106)));
    }
    [Test]
    public void InverseAffineHitUsesUntransformedGeometryExactlyOnceAndDegenerateRejects()
    {
        var geometry = new EllipticalClipGeometry(new(10,20,100,60),new(new(15,20),new(30,10),new(10,25),new(5,15)));
        var matrix = Affine2D.Compose(new(0,1,-1,0,200,0),new(1,0,0,-1,0,100));
        var p=matrix.TransformPoint(60,50);
        Assert.That(matrix.TryInversePoint(p.X,p.Y,out double x,out double y),Is.True);
        Assert.That(geometry.ContainsPoint(x,y),Is.True);
        var outside=matrix.TransformPoint(10.1,20.1);
        matrix.TryInversePoint(outside.X,outside.Y,out x,out y);
        Assert.That(geometry.ContainsPoint(x,y),Is.False);
        Assert.That(new Affine2D(0,0,0,1,0,0).TryInversePoint(p.X,p.Y,out _,out _),Is.False);
    }
}
