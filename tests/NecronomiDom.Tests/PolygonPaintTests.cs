using System;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests;

public sealed class PolygonPaintTests
{
    [TestCase("polygon(0px 0px, 100% 0px, 0px 100%)")]
    [TestCase("polygon(evenodd, 0px 0px, 100% 0px, 0px 100%)")]
    [TestCase("polygon(nonzero, 0px 0px, 100% 0px, 0px 100%) border-box")]
    public void ActualPropertyParserResolvesLocalBorderBoxPercentages(string css)
    {
        var parsed = CssPropertyParser.Parse("clip-path", CssParser.ParseComponentValueList(css));
        Assert.That(parsed, Is.Not.Null);
        var geometry = PolygonClipResolver.Resolve(parsed!.Longhands[0].Value, new Rect(40, 60, 200, 100));
        Assert.That(geometry.Count, Is.EqualTo(3));
        Assert.That(geometry[1], Is.EqualTo(new PaintPoint(200, 0)));
        Assert.That(geometry[2], Is.EqualTo(new PaintPoint(0, 100)));
        Assert.That(geometry.Contains(20, 20), Is.True);
        Assert.That(geometry.Contains(180, 90), Is.False);
    }

    [Test]
    public void GeometryCopiesMutableInputAndAcceptsEitherWinding()
    {
        PaintPoint[] input = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];
        var polygon = new ConvexPolygon(input);
        input[1] = new(0, 0);
        Assert.That(polygon[1], Is.EqualTo(new PaintPoint(100, 0)));
        var reversed = new ConvexPolygon([new(0, 100), new(100, 100), new(100, 0), new(0, 0)]);
        Assert.That(reversed.Contains(50, 50), Is.True);
        Assert.That(reversed.Contains(101, 50), Is.False);
        Assert.That(reversed.Contains(0, 50), Is.True);
    }

    [Test]
    public void QuadWithClampedZeroInnerRadiusRetainsItsFilledTriangle()
    {
        var polygon = new ConvexPolygon([new(0, 0), new(100, 0), new(0, 100), new(0, 0)]);
        Assert.That(polygon.IsEmpty, Is.False);
        Assert.That(polygon.Contains(20, 20), Is.True);
        Assert.That(polygon.Contains(80, 80), Is.False);
    }

    [Test]
    public void CollapsedTriangleProducesEmptyMaskWithoutInventingPixels()
    {
        var polygon = new ConvexPolygon([new(3, 4), new(3, 4), new(3, 4)]);
        Assert.That(polygon.IsEmpty, Is.True);
        Assert.That(polygon.Contains(3, 4), Is.False);
    }

    [Test]
    public void RejectsNonFiniteConcaveAndCrossingGeometryExplicitly()
    {
        Assert.Throws<ArgumentException>(() => new ConvexPolygon([new(0, 0), new(double.NaN, 2), new(2, 0)]));
        Assert.Throws<NotSupportedException>(() => new ConvexPolygon([new(0, 0), new(100, 0), new(40, 40), new(100, 100), new(0, 100)]));
        Assert.Throws<NotSupportedException>(() => new ConvexPolygon([new(0, 0), new(100, 100), new(0, 100), new(100, 0)]));
    }

    [TestCase("polygon(0 0, 100% 0, 0 100%) content-box")]
    [TestCase("circle(20px)")]
    public void UnimplementedClipGeometryFailsRatherThanDroppingMask(string css)
    {
        var parsed = CssPropertyParser.Parse("clip-path", CssParser.ParseComponentValueList(css));
        Assert.That(parsed, Is.Not.Null);
        Assert.Throws<NotSupportedException>(() => PolygonClipResolver.Resolve(parsed!.Longhands[0].Value, new Rect(0, 0, 100, 100)));
    }

    [Test]
    public void RoundedPaddingEdgeHitGeometryMatchesItsMaskAndCssRadiusScaling()
    {
        var shape = new RoundedRectGeometry(new Rect(20, 40, 100, 80), 30);
        Assert.That(shape.Contains(20, 40), Is.False);
        Assert.That(shape.Contains(50, 40), Is.True);
        Assert.That(shape.Contains(70, 80), Is.True);
        Assert.That(shape.Contains(119, 119), Is.False);
        Assert.That(shape.Contains(120, 80), Is.False);
        Assert.That(new RoundedRectGeometry(new Rect(0, 0, 100, 40), 100).Radius, Is.EqualTo(20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoundedRectGeometry(new Rect(0, 0, 10, 10), double.NaN));
    }

    [Test]
    public void DeclaredBoundsAndOpacityRequireFiniteLegalValues()
    {
        Assert.Throws<ArgumentException>(() => new BeginRectClipRun(new Rect(0, 0, -1, 10)));
        Assert.Throws<ArgumentException>(() => new BeginOpacityRun(new Rect(double.PositiveInfinity, 0, 10, 10), .5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BeginOpacityRun(new Rect(0, 0, 10, 10), float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BeginOpacityRun(new Rect(0, 0, 10, 10), 1.1f));
    }
}
