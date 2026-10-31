using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests;

[TestFixture]
public sealed class TextCurrentColorInheritanceTests
{
    private static (StyleEngine Styles, HTMLElement Parent, HTMLElement Child) Fixture(string parentCss, string childCss = "")
    {
        var document = HtmlImporter.Import("<!doctype html><div id=parent style='" + parentCss
            + "'><p id=child style='" + childCss + "'>Caption</p></div>", "text-currentcolor",
            WebUrl.Parse("res:///text-currentcolor.html")!).Document;
        return (new StyleEngine(document), (HTMLElement)document.GetElementById("parent")!, (HTMLElement)document.GetElementById("child")!);
    }
    private static readonly Rgba Red = new(1, 0, 0, 1), Blue = new(0, 0, 1, 1), Black = new(0, 0, 0, 1);
    [TestCase("")]
    [TestCase("-webkit-text-stroke-color:currentcolor;text-shadow:currentcolor 2px -3px")]
    public void DefaultAndExplicitCurrentColorBindToEachInheritingElement(string explicitCurrent)
    {
        var (styles, parent, child) = Fixture("color:red;-webkit-text-stroke-width:1px;text-shadow:2px -3px;" + explicitCurrent, "color:blue");
        var ancestor = TextEffectsResolver.Resolve(styles.GetComputedValues(parent));
        var descendant = TextEffectsResolver.Resolve(styles.GetComputedValues(child));
        Assert.That(ancestor.StrokeColor, Is.EqualTo(Red)); Assert.That(ancestor.Shadows.Span[0].Color, Is.EqualTo(Red));
        Assert.That(descendant.StrokeColor, Is.EqualTo(Blue)); Assert.That(descendant.Shadows.Span[0].Color, Is.EqualTo(Blue));
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("-webkit-text-stroke-color"), Is.EqualTo("rgb(0, 0, 255)"));
        Assert.That(styles.GetComputedValues(child).GetSpecifiedValue("-webkit-text-stroke-color"), Is.TypeOf<ComputedValueColor>());
    }
    [TestCase("red", 1, 0, 0)]
    [TestCase("black", 0, 0, 0)]
    public void ExplicitLiteralColorsDoNotRebindUnderChildColorOverride(string color, float red, float green, float blue)
    {
        var (styles, _, child) = Fixture("color:red;-webkit-text-stroke-width:1px;-webkit-text-stroke-color:" + color
            + ";text-shadow:" + color + " 1px 2px", "color:blue");
        var effects = TextEffectsResolver.Resolve(styles.GetComputedValues(child)); var literal = new Rgba(red, green, blue, 1);
        Assert.That(effects.StrokeColor, Is.EqualTo(literal)); Assert.That(effects.Shadows.Span[0].Color, Is.EqualTo(literal));
    }
    [Test]
    public void InheritedShadowUsesParentComputedLengthsButChildCurrentColor()
    {
        var (styles, _, child) = Fixture("font-size:20px;color:red;-webkit-text-stroke-width:.1em;text-shadow:.5em 1em .1em", "font-size:40px;color:blue");
        var effects = TextEffectsResolver.Resolve(styles.GetComputedValues(child));
        Assert.That(effects.StrokeWidth, Is.EqualTo(2));
        Assert.That(effects.Shadows.Span[0], Is.EqualTo(new TextShadow(10, 20, 2, Blue)));
    }
    [Test]
    public void ExplicitInheritRetainsDependencyWithoutChangingColorCurrentColorSemantics()
    {
        var (styles, parent, child) = Fixture("color:red;-webkit-text-stroke-width:1px;text-shadow:2px -3px", "color:blue;-webkit-text-stroke-color:inherit;text-shadow:inherit");
        Assert.That(TextEffectsResolver.Resolve(styles.GetComputedValues(child)).StrokeColor, Is.EqualTo(Blue));
        child.Style.SetProperty("color", "currentcolor");
        Assert.That(styles.GetComputedStyle(child).GetPropertyValue("color"), Is.EqualTo("rgb(255, 0, 0)"));
        Assert.That(TextEffectsResolver.Resolve(styles.GetComputedValues(child)).StrokeColor, Is.EqualTo(Red));
        Assert.That(styles.GetComputedStyle(parent).GetPropertyValue("color"), Is.EqualTo("rgb(255, 0, 0)"));
    }
    [Test]
    public void CurrentColorAndLiteralLayersRemainIndependent()
    {
        var (styles, _, child) = Fixture("color:red;text-shadow:1px 2px,black -2px -3px", "color:blue");
        var effects = TextEffectsResolver.Resolve(styles.GetComputedValues(child));
        Assert.That(effects.Shadows.Span[0].Color, Is.EqualTo(Blue)); Assert.That(effects.Shadows.Span[1].Color, Is.EqualTo(Black));
    }
    [Test]
    public void LiveCachedDeclarationsRebindOnColorMutationAndEraseObsoleteDependencies()
    {
        var (styles, parent, child) = Fixture("color:red;-webkit-text-stroke-width:1px;text-shadow:2px -3px");
        var live = styles.GetComputedValues(child); var resolved = styles.GetComputedStyle(child);
        Assert.That(TextEffectsResolver.Resolve(live).StrokeColor, Is.EqualTo(Red));
        parent.Style.SetProperty("color", "purple");
        Assert.That(resolved.GetPropertyValue("-webkit-text-stroke-color"), Is.EqualTo("rgb(128, 0, 128)"));
        child.Style.SetProperty("color", "green");
        var green = new Rgba(0, 128 / 255f, 0, 1);
        Assert.That(TextEffectsResolver.Resolve(live).StrokeColor, Is.EqualTo(green));
        Assert.That(TextEffectsResolver.Resolve(live).Shadows.Span[0].Color, Is.EqualTo(green));
        parent.Style.SetProperty("-webkit-text-stroke-color", "black"); parent.Style.SetProperty("text-shadow", "black 1px 2px");
        parent.Style.SetProperty("color", "red");
        Assert.That(TextEffectsResolver.Resolve(live).StrokeColor, Is.EqualTo(Black)); Assert.That(TextEffectsResolver.Resolve(live).Shadows.Span[0].Color, Is.EqualTo(Black));
        parent.Style.RemoveProperty("-webkit-text-stroke-color"); parent.Style.RemoveProperty("text-shadow");
        Assert.That(TextEffectsResolver.Resolve(live).StrokeColor, Is.EqualTo(green)); Assert.That(TextEffectsResolver.Resolve(live).Shadows.IsEmpty, Is.True);
    }
}
