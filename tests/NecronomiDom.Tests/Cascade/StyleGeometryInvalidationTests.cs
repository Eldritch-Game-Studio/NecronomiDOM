using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Media;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class StyleGeometryInvalidationTests
{
    private static (Document Doc, StyleEngine Styles) Load(string css, string body)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" + css + "</style><body>" + body + "</body>",
            "geometry-invalidation", WebUrl.Parse("res:///geometry-invalidation.html")!).Document;
        var styles = new StyleEngine(doc);
        Warm(doc, styles);
        return (doc, styles);
    }
    private static void Warm(Document doc, StyleEngine styles)
    {
        foreach (Element element in doc.QuerySelectorAll("*")) styles.GetComputedStyle(element).GetPropertyValue("color");
    }
    private static void AssertMatchesFull(Document doc, StyleEngine styles)
    {
        var reference = new StyleEngine(doc);
        foreach (Element element in doc.QuerySelectorAll("*"))
        {
            var actual = styles.GetComputedStyle(element);
            var expected = reference.GetComputedStyle(element);
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                string name = expected.Item(i);
                Assert.That(actual.GetPropertyValue(name), Is.EqualTo(expected.GetPropertyValue(name)), element.Id + " " + name);
            }
        }
    }

    [Test]
    public void DescendantPercentAndEmBoxesStillResolveAgainstTheNewGeometry()
    {
        var (doc, styles) = Load("html,body {margin:0;padding:0;font:20px monospace} #c {width:50%;height:2em}",
            "<div id=p style='width:100px;height:60px'><div id=c></div></div>");
        var layout = new FlowLayout(styles, new Metrics(), "monospace");
        var first = layout.Layout(doc, 800, 600).Boxes.Single(b => b.Element.Id == "c").Bounds;
        long before = styles.ComputedEntryCount;
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("width", "200px");
        var next = layout.Layout(doc, 800, 600).Boxes.Single(b => b.Element.Id == "c").Bounds;
        Assert.That(styles.ComputedEntryCount - before, Is.EqualTo(1));
        Assert.That(first.Width, Is.EqualTo(50)); Assert.That(next.Width, Is.EqualTo(100));
        Assert.That(first.Height, Is.EqualTo(40)); Assert.That(next.Height, Is.EqualTo(40));
        AssertMatchesFull(doc, styles);
    }

    [TestCase("#c {width:inherit}", "")]
    [TestCase("", "style='width:inherit'")]
    [TestCase("#c {all:inherit}", "")]
    public void ExplicitGeometryInheritanceKeepsTheSubtreeFallback(string css, string inline)
    {
        var (doc, styles) = Load(css, "<div id=p style='width:100px'><div id=c " + inline + "><span></span></div></div>");
        long before = styles.ComputedEntryCount;
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("width", "200px");
        Warm(doc, styles);
        Assert.That(styles.ComputedEntryCount - before, Is.EqualTo(3));
        Assert.That(styles.GetComputedStyle(doc.GetElementById("c")!).GetPropertyValue("width"), Is.EqualTo("200px"));
        AssertMatchesFull(doc, styles);
    }

    [TestCase("color:red", "width:100px")]
    [TestCase("width:100px", "color:blue")]
    [TestCase("--size:10px;width:100px", "--size:20px;width:200px")]
    [TestCase("font-size:20px;width:100px", "font-size:30px;width:200px")]
    public void AddingOrRemovingInheritedInlineValuesRestylesDescendants(string previous, string next)
    {
        var (doc, styles) = Load("#c {width:var(--size,5px);height:2em}",
            "<div id=p style='" + previous + "'><div id=c><span></span></div></div>");
        long before = styles.ComputedEntryCount;
        doc.GetElementById("p")!.SetAttribute("style", next); Warm(doc, styles);
        Assert.That(styles.ComputedEntryCount - before, Is.EqualTo(3));
        AssertMatchesFull(doc, styles);
    }

    [TestCase("[style*='200'] #c {color:red}")]
    [TestCase(":is([style*='200']) #c {color:red}")]
    [TestCase("@scope ([style*='200']) {#c {color:red}}")]
    [TestCase("#p {--copy:attr(style)} #c {content:var(--copy)}")]
    [TestCase("@container (min-width:1px) {#c {color:red}}")]
    [TestCase("@property --size {syntax:'<length>';inherits:true;initial-value:10px} #c {width:var(--size)}")]
    public void StyleAttributeAndContainerDependenciesKeepTheSubtreeFallback(string css)
    {
        var (doc, styles) = Load(css, "<div id=p style='width:100px'><div id=c></div></div>");
        long before = styles.ComputedEntryCount;
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("width", "200px"); Warm(doc, styles);
        Assert.That(styles.ComputedEntryCount - before, Is.EqualTo(2));
        AssertMatchesFull(doc, styles);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExternalComputedContextsKeepTheSubtreeFallback(bool container)
    {
        var (doc, styles) = Load("", "<div id=p style='width:100px'><div id=c></div></div>");
        if (container) styles.ContainerEnvironment = (_, _) => new MediaEnvironment();
        else styles.ValueContext = _ => new ComputedValueContext();
        long before = styles.ComputedEntryCount;
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("width", "200px"); Warm(doc, styles);
        Assert.That(styles.ComputedEntryCount - before, Is.EqualTo(2));
        AssertMatchesFull(doc, styles);
    }

    [Test]
    public void BatchedNewChildInheritanceAndParentGeometryMatchFullCascade()
    {
        var (doc, styles) = Load("#g {width:inherit}",
            "<div id=p style='width:100px'><div id=c style='width:50px'><div id=g></div></div></div>");
        ((HTMLElement)doc.GetElementById("c")!).Style.SetProperty("width", "inherit");
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("width", "200px");
        AssertMatchesFull(doc, styles);
        Assert.That(styles.GetComputedStyle(doc.GetElementById("g")!).GetPropertyValue("width"), Is.EqualTo("200px"));
    }

    private sealed class Metrics : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false)
            => new(text.Length * 8, 12, 4);
    }
}
