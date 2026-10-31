using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class StyleInlineOpacityTests
{
    private static (StyleEngine Styles, HTMLElement Element) Load(string css = "", string children = "")
    {
        var document = HtmlImporter.Import("<!doctype html><style>" + css +
            "</style><div id=p style='opacity:.5'>" + children + "</div>",
            "opacity-delta", WebUrl.Parse("res:///opacity-delta.html")!).Document;
        var styles = new StyleEngine(document);
        var element = (HTMLElement)document.GetElementById("p")!;
        foreach (var node in document.QuerySelectorAll("*"))
            if (node is Element current) styles.GetComputedStyle(current);
        return (styles, element);
    }

    [Test]
    public void WinningDeltaSurvivesComputedReadsAndIsConsumedOnce()
    {
        var (styles, element) = Load();
        element.Style.SetProperty("opacity", ".4");
        Assert.That(styles.GetComputedStyle(element).GetPropertyValue("opacity"), Is.EqualTo("0.4"));
        Assert.That(styles.TryConsumeInlineOpacityChange(element), Is.True);
        Assert.That(styles.TryConsumeInlineOpacityChange(element), Is.False);
    }

    [TestCase("[style]{width:10px}", "")]
    [TestCase("#p{opacity:.2!important}", "")]
    [TestCase("#p>div{opacity:inherit}", "<div></div>")]
    [TestCase("#p::before{opacity:inherit}", "")]
    public void SelectorCascadeAndInheritanceDependenciesRejectCompositorOnlyChange(string css, string children)
    {
        var (styles, element) = Load(css, children);
        styles.GetComputedStyle(element, "before");
        element.Style.SetProperty("opacity", ".4");
        Assert.That(styles.TryConsumeInlineOpacityChange(element), Is.False);
    }

    [Test]
    public void BroaderMutationAndSheetInvalidationRetirePendingDelta()
    {
        var (styles, element) = Load(".changed{width:20px}");
        element.Style.SetProperty("opacity", ".4");
        styles.GetComputedStyle(element);
        element.ClassName = "changed";
        Assert.That(styles.TryConsumeInlineOpacityChange(element), Is.False);
        element.Style.SetProperty("opacity", ".3");
        styles.GetComputedStyle(element);
        styles.Invalidate();
        Assert.That(styles.TryConsumeInlineOpacityChange(element), Is.False);
    }
}
