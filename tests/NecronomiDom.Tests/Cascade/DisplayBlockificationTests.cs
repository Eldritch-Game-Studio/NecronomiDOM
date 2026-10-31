using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

/// <summary>CSS Display §2.7 / Flexbox §4: computed box type, not authored CSS or renderer exceptions.</summary>
public sealed class DisplayBlockificationTests
{
    private static Document NewDocument() => new Document().Implementation.CreateHTMLDocument("blockification");
    private static HTMLElement Add(Document doc, Element parent, string tag, string display)
    {
        var element = (HTMLElement)doc.CreateElement(tag); element.Style.SetProperty("display", display);
        parent.AppendChild(element); return element;
    }
    [TestCase("flex")]
    [TestCase("inline-flex")]
    [TestCase("grid")]
    [TestCase("inline-grid")]
    public void NearestBoxAncestorSkipsContentsButNotARealWrapperAndRestylesLive(string display)
    {
        var doc = NewDocument(); var parent = Add(doc, doc.Body!, "div", display);
        var wrapper = Add(doc, parent, "div", "contents"); var child = Add(doc, wrapper, "span", "inline");
        var computed = doc.DefaultView.GetComputedStyle(child);
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("block"));
        wrapper.Style.SetProperty("display", "block");
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("inline"));
        wrapper.Style.SetProperty("display", "contents"); parent.Style.SetProperty("display", "block");
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("inline"));
        parent.Style.SetProperty("display", display);
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("block"));
        doc.Body!.AppendChild(child);
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("inline"));
        parent.AppendChild(child);
        Assert.That(doc.DefaultView.GetComputedStyle(child), Is.SameAs(computed));
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("block"));
    }
    [Test]
    public void StableInlineDisplayKeepsScalarGeometryRefreshAndDoesNotRecascadeChildren()
    {
        var doc = NewDocument(); var parent = Add(doc, doc.Body!, "div", "flex");
        parent.Style.SetProperty("width", "100px"); var child = Add(doc, parent, "span", "inline");
        var styles = new StyleEngine(doc); var computed = styles.GetComputedStyle(child);
        long entries = styles.ComputedEntryCount, refreshes = styles.GeometryRefreshCount;
        parent.Style.SetProperty("width", "120px");
        Assert.That(styles.GetComputedStyle(parent).GetPropertyValue("width"), Is.EqualTo("120px"));
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("block"));
        Assert.That(styles.ComputedEntryCount - entries, Is.EqualTo(1));
        Assert.That(styles.GeometryRefreshCount - refreshes, Is.EqualTo(1));
    }
    [Test]
    public void AddingRemovingAndChangingInlineDisplayPriorityRecomputesChildContext()
    {
        var doc = NewDocument(); var parent = Add(doc, doc.Body!, "div", "block");
        parent.Style.SetProperty("width", "100px"); var child = Add(doc, parent, "span", "inline");
        var computed = doc.DefaultView.GetComputedStyle(child);
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("inline"));
        parent.Style.RemoveProperty("display");
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("inline"));
        parent.Style.SetProperty("display", "flex");
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("block"));
        parent.Style.RemoveProperty("display");
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("inline"));
        parent.Style.SetProperty("display", "grid", "important");
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("block"));
        parent.Style.SetProperty("display", "block");
        Assert.That(computed.GetPropertyValue("display"), Is.EqualTo("inline"));
    }
    [TestCase("flex")]
    [TestCase("grid")]
    public void ComposedSlotParentAndHiddenAncestorsStillDetermineBlockification(string display)
    {
        var doc = NewDocument(); var hidden = Add(doc, doc.Body!, "div", "none");
        var host = Add(doc, hidden, "div", display);
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var slot = (HTMLElement)doc.CreateElement("slot"); slot.Style.SetProperty("display", "contents"); shadow.AppendChild(slot);
        var child = Add(doc, host, "span", "inline");
        Assert.That(doc.DefaultView.GetComputedStyle(child).GetPropertyValue("display"), Is.EqualTo("block"));
        slot.Style.SetProperty("display", "block");
        Assert.That(doc.DefaultView.GetComputedStyle(child).GetPropertyValue("display"), Is.EqualTo("inline"));
        slot.Style.SetProperty("display", "contents");
        Assert.That(doc.DefaultView.GetComputedStyle(child).GetPropertyValue("display"), Is.EqualTo("block"));
    }
    [TestCase("inline-block", "block")]
    [TestCase("inline flow-root", "block")]
    [TestCase("run-in flow-root", "block")]
    [TestCase("flow-root", "flow-root")]
    [TestCase("inline flow-root list-item", "flow-root list-item")]
    public void LegacyConversionIsTheSameForRootPositionedAndItemContexts(string specified, string expected)
    {
        foreach (var context in new[] {
            new ComputedValueContext { IsRoot = true }, new ComputedValueContext { IsFlexOrGridItem = true },
            new ComputedValueContext { PropertyValue = p => p == "position" ? new NecronomiDom.Css.Values.CssKeywordValue("absolute") : null } })
            Assert.That(ComputedValues.ComputedPropertyTests.Resolve("display", specified, context), Is.EqualTo(expected));
    }
}
