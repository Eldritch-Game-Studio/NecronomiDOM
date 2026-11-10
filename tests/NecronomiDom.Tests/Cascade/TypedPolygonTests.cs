using System;
using System.Collections.Generic;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NecronomiDom.Paint;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class TypedPolygonTests
{
    private static HTMLElement Element()
    {
        var doc = new Document().Implementation.CreateHTMLDocument("typed polygon");
        _ = doc.DefaultView;
        var element = (HTMLElement)doc.CreateElement("div");
        doc.Body!.AppendChild(element);
        return element;
    }
    private static CSSPolygonPoint Point(double x, double y, string unit = "px") => new(new(x, unit), new(y, unit));
    private static CSSPolygonPoint[] Triangle() => new[] { Point(0, 0), Point(90, 0), Point(90, 90) };

    [Test]
    public void SharedCallerStorageAndReturnedSnapshotsCannotMutateDeclarations()
    {
        var first = Element(); var second = Element(); var third = Element();
        var points = Triangle();
        var value = new CSSPolygonValue(points);
        first.AttributeStyleMap.Set("clip-path", value);
        second.AttributeStyleMap.Set("clip-path", value);
        var read = first.AttributeStyleMap.Get("clip-path")!.Value;
        var computedRead = first.ComputedStyleMap().Get("clip-path")!.Value;
        var anotherRead = first.AttributeStyleMap.Get("clip-path")!.Value;
        string original = read.ToString();
        Assert.That(read.Kind, Is.EqualTo(CSSStyleValueKind.Polygon));
        Assert.That(read.TryGetPolygon(out var snapshot), Is.True);
        Assert.That(first.Style.GetSpecifiedValue("clip-path"), Is.Not.SameAs(second.Style.GetSpecifiedValue("clip-path")));
        points[1] = Point(50, 2);
        Assert.That(first.Style.GetPropertyValue("clip-path"), Is.EqualTo(original), "Caller edits alone must be invisible.");
        first.AttributeStyleMap.Set("clip-path", value);
        Assert.That(first.Style.GetPropertyValue("clip-path"), Is.Not.EqualTo(original));
        Assert.That(second.Style.GetPropertyValue("clip-path"), Is.EqualTo(original));
        Assert.That(read.ToString(), Is.EqualTo(original));
        Assert.That(computedRead.TryGetPolygon(out var computedSnapshot), Is.True);
        Assert.That(computedRead.ToString(), Is.EqualTo(original));
        Assert.That(computedSnapshot.ToString(), Is.EqualTo(original));
        Assert.That(snapshot.ToString(), Is.EqualTo(original));
        third.AttributeStyleMap.Set("clip-path", read);
        Assert.That(third.Style.GetPropertyValue("clip-path"), Is.EqualTo(original));
        ((CSSPolygonPoint[])snapshot.Points)[1] = Point(20, 0);
        Assert.That(anotherRead.ToString(), Is.EqualTo(original), "Each returned snapshot owns its points.");
        Assert.That(second.Style.GetPropertyValue("clip-path"), Is.EqualTo(original));
        Assert.That(third.Style.GetPropertyValue("clip-path"), Is.EqualTo(original));
    }

    [TestCase(double.NaN, "px")]
    [TestCase(double.PositiveInfinity, "percent")]
    [TestCase(double.NegativeInfinity, "px")]
    [TestCase(2, "em")]
    [TestCase(0, "number")]
    [TestCase(90, "deg")]
    public void InvalidLateCoordinateIsRejectedBeforeAnyDeclarationMutation(double coordinate, string unit)
    {
        var element = Element();
        var points = Triangle();
        var value = new CSSPolygonValue(points);
        element.AttributeStyleMap.Set("clip-path", value);
        var slot = (TypedPolygonSlot)element.Style.GetSpecifiedValue("clip-path")!;
        long revision = slot.Revision;
        string before = element.GetAttribute("style")!;
        points[0] = Point(8, 8); // A valid earlier point must not be partially copied.
        points[2] = new(new(40, "px"), new(coordinate, unit));
        Assert.Throws<DomTypeError>(() => element.AttributeStyleMap.Set("clip-path", value));
        Assert.That(element.Style.GetSpecifiedValue("clip-path"), Is.SameAs(slot));
        Assert.That(slot.Revision, Is.EqualTo(revision));
        Assert.That(element.GetAttribute("style"), Is.EqualTo(before));
    }

    [Test]
    public void InvalidCountDefaultAndWrongPropertyLeaveExistingValueIntact()
    {
        var element = Element();
        var points = new List<CSSPolygonPoint>(Triangle());
        var value = new CSSPolygonValue(points);
        element.AttributeStyleMap.Set("clip-path", value);
        string before = element.GetAttribute("style")!;
        Assert.Throws<DomTypeError>(() => element.AttributeStyleMap.Set("width", value));
        Assert.Throws<DomTypeError>(() => element.AttributeStyleMap.Set("clip-path", default(CSSPolygonValue)));
        points.RemoveAt(2);
        Assert.Throws<DomTypeError>(() => element.AttributeStyleMap.Set("clip-path", value));
        Assert.Throws<DomTypeError>(() => new CSSPolygonValue(points));
        Assert.Throws<ArgumentNullException>(() => new CSSPolygonValue(null!));
        Assert.That(element.GetAttribute("style"), Is.EqualTo(before));
    }

    [Test]
    public void SameShapeUpdatesReuseSlotAndAllocateNothingAfterWarmup()
    {
        var element = Element();
        var map = element.AttributeStyleMap;
        var points = Triangle();
        var value = new CSSPolygonValue(points);
        for (int i = 0; i < 128; i++)
        { points[1] = Point(128 + i, 0); map.Set("clip-path", value); }
        var slot = element.Style.GetSpecifiedValue("clip-path");
        long allocated = AllocationWindow.Measure(128, i =>
        { points[1] = Point(256 + i, 0); map.Set("clip-path", value); });
        Assert.That(allocated, Is.Zero);
        Assert.That(element.Style.GetSpecifiedValue("clip-path"), Is.SameAs(slot));
        Assert.That(element.Style.GetPropertyValue("clip-path"), Is.EqualTo("polygon(0px 0px, 383px 0px, 90px 90px)"));
    }

    [Test]
    public void VertexCountAndCoordinateUnitsCanChangeWithoutSharingOldStorage()
    {
        var element = Element();
        var points = Triangle();
        element.AttributeStyleMap.Set("clip-path", new CSSPolygonValue(points));
        var old = element.AttributeStyleMap.Get("clip-path")!.Value;
        var slot = element.Style.GetSpecifiedValue("clip-path");
        points[1] = Point(75, 0, "percent");
        element.AttributeStyleMap.Set("clip-path", new CSSPolygonValue(points));
        Assert.That(element.Style.GetSpecifiedValue("clip-path"), Is.SameAs(slot));
        var rectangle = new[] { Point(0, 0), Point(100, 0, "percent"), Point(100, 100, "percent"), Point(0, 100, "percent") };
        element.AttributeStyleMap.Set("clip-path", new CSSPolygonValue(rectangle));
        Assert.That(element.Style.GetSpecifiedValue("clip-path"), Is.Not.SameAs(slot));
        Assert.That(old.ToString(), Is.EqualTo("polygon(0px 0px, 90px 0px, 90px 90px)"));
        element.AttributeStyleMap.Set("clip-path", "polygon(0px 0px, 40px 0px, 40px 40px)");
        Assert.That(element.AttributeStyleMap.Get("clip-path")!.Value.ToString(), Is.EqualTo("polygon(0px 0px, 40px 0px, 40px 40px)"));
    }

    [Test]
    public void UsedGeometryKeepsExistingConvexFiniteAndCollapsedShapeRules()
    {
        var element = Element();
        var points = new[] { Point(0, 0), Point(0, 0), Point(0, 0) };
        element.AttributeStyleMap.Set("clip-path", new CSSPolygonValue(points));
        var collapsed = PolygonClipResolver.Resolve(element.Style.GetSpecifiedValue("clip-path")!, new(0, 0, 100, 100));
        Assert.That(collapsed.IsEmpty, Is.True);
        var concave = new[] { Point(0, 0), Point(100, 0), Point(30, 30), Point(100, 100), Point(0, 100) };
        element.AttributeStyleMap.Set("clip-path", new CSSPolygonValue(concave));
        Assert.Throws<NotSupportedException>(() => PolygonClipResolver.Resolve(element.Style.GetSpecifiedValue("clip-path")!, new(0, 0, 100, 100)));
        var huge = new[] { Point(0, 0), Point(double.MaxValue, 0, "percent"), Point(0, 100, "percent") };
        element.AttributeStyleMap.Set("clip-path", new CSSPolygonValue(huge));
        Assert.Throws<ArgumentException>(() => PolygonClipResolver.Resolve(element.Style.GetSpecifiedValue("clip-path")!, new(0, 0, 1000, 1000)));
    }
}
