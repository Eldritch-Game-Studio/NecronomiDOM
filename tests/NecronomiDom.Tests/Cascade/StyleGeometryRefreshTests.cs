using System;
using System.Globalization;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class StyleGeometryRefreshTests
{
    private static (Document Doc, StyleEngine Styles, HTMLElement Element) Load(string css = "", string inline = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html{--size:20px}#p{background-color:red;" +
            "transform:translate(20%,30%);transform-origin:25% 75%;perspective-origin:10% 90%}" + css +
            "</style><div id=p style='left:1px;top:2%;width:100px;height:80px;opacity:.5;rotate:1deg;display:flex;" + inline + "'><span id=c>text</span></div>",
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

    [TestCase("pointer-events:none")]
    [TestCase("pointer-events:none;background-color:var(--tone);--tone:green")]
    [TestCase("color:rgb(23 40 70 / .7);pointer-events:none")]
    public void MovingNotesWithUnchangedPaintAndInputDeclarationsDoNotRestyleDescendants(string unchanged)
    {
        var (doc, styles, element) = Load(inline: "bottom:100%;" + unchanged);
        var (controlDoc, reference, control) = Load(inline: "bottom:100%;" + unchanged);
        var child = doc.GetElementById("c")!;
        var controlChild = controlDoc.GetElementById("c")!;
        styles.GetComputedValues(child);
        for (int i = 0; i < 40; i++)
        {
            string property = i % 2 == 0 ? "bottom" : "height";
            string value = (i + 1).ToString(CultureInfo.InvariantCulture) + (property == "bottom" ? "%" : "px");
            element.Style.SetProperty(property, value);
            control.Style.SetProperty(property, value);
            reference.Invalidate();
            Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
            Compare(styles.GetComputedValues(element), reference.GetComputedValues(control));
            long afterParent = styles.ComputedEntryCount;
            Compare(styles.GetComputedValues(child), reference.GetComputedValues(controlChild));
            Assert.That(styles.ComputedEntryCount, Is.EqualTo(afterParent), "An independent note position must not invalidate its trail/glyph.");
        }
        Assert.That(styles.GeometryRefreshCount, Is.EqualTo(40), "Exercise the actual mixed-inline fast path, not just equivalent full recomputation.");
    }

    [TestCase("pointer-events", "auto", "")]
    [TestCase("pointer-events", "none", "important")]
    [TestCase("color", "blue", "")]
    [TestCase("--tone", "blue", "")]
    [TestCase("display", "none", "")]
    public void BatchedGeometryAndChangedNongeometryOrPriorityStillRecascade(string property, string value, string priority)
    {
        const string inline = "pointer-events:none;color:red;--tone:red;background-color:var(--tone)";
        var (doc, styles, element) = Load(inline: inline);
        var (controlDoc, reference, control) = Load(inline: inline);
        styles.GetComputedValues(doc.GetElementById("c")!);
        element.Style.SetProperty("width", "200px");
        control.Style.SetProperty("width", "200px");
        element.Style.SetProperty(property, value, priority);
        control.Style.SetProperty(property, value, priority);
        reference.Invalidate();
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Compare(styles.GetComputedValues(element), reference.GetComputedValues(control));
        Compare(styles.GetComputedValues(doc.GetElementById("c")!), reference.GetComputedValues(controlDoc.GetElementById("c")!));
        Assert.That(styles.GeometryRefreshCount, Is.Zero);
    }

    [TestCase("#p[style*='200px'] span{color:blue}")]
    [TestCase("#p::before{content:attr(style)}")]
    public void ObservableStyleAttributesCannotUseMixedInlineGeometryRefresh(string css)
    {
        var (doc, styles, element) = Load(css, "pointer-events:none");
        var (controlDoc, reference, control) = Load(css, "pointer-events:none");
        styles.GetComputedValues(doc.GetElementById("c")!);
        element.Style.SetProperty("width", "200px");
        control.Style.SetProperty("width", "200px");
        reference.Invalidate();
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Compare(styles.GetComputedValues(doc.GetElementById("c")!), reference.GetComputedValues(controlDoc.GetElementById("c")!));
        Assert.That(styles.GeometryRefreshCount, Is.Zero);
    }

    [TestCase("#c{height:inherit}")]
    [TestCase("#c{container-type:size}")]
    public void InheritedGeometryAndNestedContainersRetainSubtreeReconciliation(string css)
    {
        var (doc, styles, element) = Load(css, "pointer-events:none");
        var (controlDoc, reference, control) = Load(css, "pointer-events:none");
        styles.GetComputedValues(doc.GetElementById("c")!);
        element.Style.SetProperty("height", "160px");
        control.Style.SetProperty("height", "160px");
        reference.Invalidate();
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Compare(styles.GetComputedValues(doc.GetElementById("c")!), reference.GetComputedValues(controlDoc.GetElementById("c")!));
        Assert.That(styles.GeometryRefreshCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MovingNumericPolygonsAndAbsolutePaintMatchTheFullCascadeWithoutRestylingChildren(bool typedPaint)
    {
        const string inline = "clip-path:polygon(0% 0%,80% 0%,80% 90%);background-color:rgba(20,40,60,.7)";
        var (doc, styles, element) = Load(inline: inline);
        var (controlDoc, reference, control) = Load(inline: inline);
        var live = styles.GetComputedStyle(element);
        var typed = styles.GetComputedValues(element);
        var child = doc.GetElementById("c")!;
        var controlChild = controlDoc.GetElementById("c")!;
        styles.GetComputedValues(child);
        for (int i = 0; i < 40; i++)
        {
            string clip = FormattableString.Invariant($"polygon(0px 0px,{20 + i}% 2px,80% 90%)");
            string color = FormattableString.Invariant($"rgba({30 + i},70,120,0.73)");
            element.Style.SetProperty("clip-path", clip);
            control.Style.SetProperty("clip-path", clip);
            if (typedPaint)
            {
                var sample = CSSStyleValue.Parse("background-color", color);
                element.AttributeStyleMap.Set("background-color", sample);
                control.AttributeStyleMap.Set("background-color", sample);
            }
            else
            {
                element.Style.SetProperty("background-color", color);
                control.Style.SetProperty("background-color", color);
            }
            reference.Invalidate();
            Compare(live, reference.GetComputedStyle(control));
            Compare(typed, reference.GetComputedValues(control));
            long entries = styles.ComputedEntryCount;
            Compare(styles.GetComputedValues(child), reference.GetComputedValues(controlChild));
            Assert.That(styles.ComputedEntryCount, Is.EqualTo(entries), "Independent paint must not invalidate a child's cascade.");
        }
        Assert.That(styles.GeometryRefreshCount, Is.EqualTo(40), "Exercise local paint refresh, not equivalent full recomputation.");
    }

    [TestCase("", "clip-path", "polygon(0px 0px,2em 0px,30px 40px)", "")]
    [TestCase("", "clip-path", "polygon(0px 0px,calc(10px + 2em) 0px,30px 40px)", "")]
    [TestCase("", "clip-path", "circle(20%)", "")]
    [TestCase("", "clip-path", "inherit", "")]
    [TestCase("", "background-color", "currentcolor", "")]
    [TestCase("", "background-color", "CanvasText", "")]
    [TestCase("", "background-color", "var(--tone)", "")]
    [TestCase("", "background-color", "rgb(from currentcolor r g b)", "")]
    [TestCase("", "background-color", "rgb(calc(30) 70 120)", "")]
    [TestCase("#p{background-color:blue!important}", "background-color", "rgba(50,60,70,.4)", "")]
    [TestCase("", "background-color", "rgba(50,60,70,.4)", "important")]
    public void ContextDependentShapesColorsAndCascadePriorityRetainFullResolution(string css, string property, string value, string priority)
    {
        const string inline = "--tone:green;clip-path:polygon(0px 0px,80px 0px,80px 90px);background-color:rgba(20,40,60,.7)";
        var (_, styles, element) = Load(css, inline);
        var (_, reference, control) = Load(css, inline);
        element.Style.SetProperty(property, value, priority);
        control.Style.SetProperty(property, value, priority);
        reference.Invalidate();
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Compare(styles.GetComputedValues(element), reference.GetComputedValues(control));
        Assert.That(styles.GeometryRefreshCount, Is.Zero);
    }

    [TestCase("#c{clip-path:inherit}")]
    [TestCase("#c{background-color:inherit}")]
    [TestCase("#p[style*='50'] span{color:blue}")]
    [TestCase("#p::before{content:attr(style)}")]
    public void DescendantInheritanceAndObservableStyleAttributesStillReconcilePaintSubtrees(string css)
    {
        const string inline = "clip-path:polygon(0px 0px,80px 0px,80px 90px);background-color:rgba(20,40,60,.7)";
        var (doc, styles, element) = Load(css, inline);
        var (controlDoc, reference, control) = Load(css, inline);
        styles.GetComputedValues(doc.GetElementById("c")!);
        element.Style.SetProperty("clip-path", "polygon(0px 0px,50px 0px,80px 90px)");
        control.Style.SetProperty("clip-path", "polygon(0px 0px,50px 0px,80px 90px)");
        element.Style.SetProperty("background-color", "rgba(50,60,70,.4)");
        control.Style.SetProperty("background-color", "rgba(50,60,70,.4)");
        reference.Invalidate();
        Compare(styles.GetComputedStyle(element), reference.GetComputedStyle(control));
        Compare(styles.GetComputedValues(doc.GetElementById("c")!), reference.GetComputedValues(controlDoc.GetElementById("c")!));
        Assert.That(styles.GeometryRefreshCount, Is.Zero);
    }
}
