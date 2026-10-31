using System;
using System.Globalization;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class StyleGeometryRefreshTests
{
    private static (Document Doc, StyleEngine Styles, HTMLElement Element) Load(string css = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html{--size:20px}#p{background-color:red;" +
            "transform:translate(20%,30%);transform-origin:25% 75%;perspective-origin:10% 90%}" + css +
            "</style><div id=p style='left:1px;top:2%;width:100px;height:80px;opacity:.5;rotate:1deg;display:flex'><span>text</span></div>",
            "geometry-refresh", WebUrl.Parse("res:///geometry-refresh.html")!).Document;
        var styles = new StyleEngine(doc);
        var element = (HTMLElement)doc.GetElementById("p")!;
        styles.GetComputedStyle(element);
        styles.GetComputedValues(element);
        return (doc, styles, element);
    }

    private static void Compare(CSSStyleDeclaration actual, CSSStyleDeclaration expected)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
        {
            string name = expected.Item(i);
            Assert.That(actual.GetPropertyValue(name), Is.EqualTo(expected.GetPropertyValue(name)), name);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedScalarAnimationMatchesFullCascadeIncludingLogicalAndResolvedViews(bool rtl)
    {
        var (_, styles, element) = Load(rtl ? "#p{direction:rtl}" : "");
        var (_, reference, control) = Load(rtl ? "#p{direction:rtl}" : "");
        var live = styles.GetComputedStyle(element);
        var typed = styles.GetComputedValues(element);
        var random = new Random(714);
        string[] properties = { "left", "top", "width", "height", "opacity", "rotate" };
        for (int i = 0; i < 40; i++)
        {
            string property = properties[i % properties.Length];
            string value = random.Next(1, 90).ToString(CultureInfo.InvariantCulture) +
                (property == "opacity" ? "%" : property == "rotate" ? "deg" : i % 2 == 0 ? "px" : "%");
            element.Style.SetProperty(property, value);
            control.Style.SetProperty(property, value);
            reference.Invalidate();
            Compare(live, reference.GetComputedStyle(control));
            Compare(typed, reference.GetComputedValues(control));
        }
        Assert.That(styles.GeometryRefreshCount, Is.GreaterThan(30), "The differential test must exercise the actual fast path");
    }

    [TestCase("#p{width:40px!important}", "width", "200px", "")]
    [TestCase("", "width", "200px", "important")]
    [TestCase("", "width", "2em", "")]
    [TestCase("", "width", "var(--size)", "")]
    [TestCase("", "width", "inherit", "")]
    [TestCase("", "display", "none", "")]
    public void NonwinningOrContextSensitiveChangesUseTheFullCascade(string css, string property, string value, string priority)
    {
        var (_, styles, element) = Load(css);
        var (_, reference, control) = Load(css);
        element.Style.SetProperty(property, value, priority);
        control.Style.SetProperty(property, value, priority);
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Assert.That(styles.GeometryRefreshCount, Is.Zero);
    }

    [Test]
    public void BroaderBatchedInvalidationCannotReuseAnEarlierGeometryOnlySnapshot()
    {
        var (doc, styles, element) = Load(".changed{color:blue;width:30px!important}");
        var (_, reference, control) = Load(".changed{color:blue;width:30px!important}");
        element.Style.SetProperty("width", "200px");
        control.Style.SetProperty("width", "200px");
        styles.GetComputedStyle(element);
        long before = styles.GeometryRefreshCount;
        element.ClassName = control.ClassName = "changed";
        element.Style.SetProperty("width", "300px");
        control.Style.SetProperty("width", "300px");
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Assert.That(styles.GeometryRefreshCount, Is.EqualTo(before));
        element.Style.RemoveProperty("width");
        control.Style.RemoveProperty("width");
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Assert.That(doc.GetElementById("p"), Is.SameAs(element));
    }
}
