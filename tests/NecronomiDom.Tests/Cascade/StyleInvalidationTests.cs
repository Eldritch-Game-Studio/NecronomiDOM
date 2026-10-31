using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;
using System.Linq;

namespace NecronomiDom.Tests.Cascade;

/// <summary>Subtree style invalidation: DOM changes restyle only the affected subtree and still produce fresh styles.</summary>
[TestFixture]
public sealed class StyleInvalidationTests
{
    private const string Red = "rgb(255, 0, 0)", Green = "rgb(0, 128, 0)", Blue = "rgb(0, 0, 255)";

    private static (Document Doc, StyleEngine Styles) Load(string css, string body)
    {
        var doc = HtmlImporter.Import("<!doctype html><style>" + css + "</style><body>" + body + "</body>",
            "invalidation", WebUrl.Parse("res:///invalidation.html")!).Document;
        var styles = new StyleEngine(doc);
        StyleAll(doc, styles);
        return (doc, styles);
    }
    private static void StyleAll(Document doc, StyleEngine styles)
    {
        foreach (Element element in doc.QuerySelectorAll("*")) styles.GetComputedStyle(element).GetPropertyValue("color");
    }
    /// <summary>Elements recomputed by one full style read after the change.</summary>
    private static long Restyled(Document doc, StyleEngine styles)
    {
        long before = styles.ComputedEntryCount;
        StyleAll(doc, styles);
        return styles.ComputedEntryCount - before;
    }
    private static string Color(StyleEngine styles, Document doc, string id)
        => styles.GetComputedStyle(doc.GetElementById(id)!).GetPropertyValue("color");
    private static string Background(StyleEngine styles, Document doc, string id)
        => styles.GetComputedStyle(doc.GetElementById(id)!).GetPropertyValue("background-color");

    [Test]
    public void UnchangedDocumentRestylesNothing()
    {
        var (doc, styles) = Load("p { color: red }", "<p id=a>A</p><p id=b>B</p>");
        Assert.That(Restyled(doc, styles), Is.Zero);
    }

    [Test]
    public void AttributeChangeRestylesOnlyThatSubtree()
    {
        var (doc, styles) = Load(".on { color: green }",
            "<div id=a><span id=a1>x</span></div><div id=b><span id=b1>y</span><span id=b2>z</span></div>");
        doc.GetElementById("a")!.ClassName = "on";
        Assert.That(Restyled(doc, styles), Is.EqualTo(2));
        Assert.That(Color(styles, doc, "a1"), Is.EqualTo(Green), "descendants inherit the restyled value");
        Assert.That(Color(styles, doc, "b1"), Is.Not.EqualTo(Green));
    }

    [Test]
    public void InlineStyleChangeReachesInheritingDescendants()
    {
        var (doc, styles) = Load("", "<div id=a><p id=a1><span id=a2>x</span></p></div><div id=b></div>");
        doc.GetElementById("a")!.SetAttribute("style", "color: blue");
        Assert.That(Restyled(doc, styles), Is.EqualTo(3));
        Assert.That(Color(styles, doc, "a2"), Is.EqualTo(Blue));
    }

    [Test]
    public void SiblingCombinatorsRestyleTheParentsChildren()
    {
        var (doc, styles) = Load(".x + p { color: red } .x ~ div span { color: blue }",
            "<section id=s><p id=a>a</p><p id=b>b</p><div id=c><span id=c1>c</span></div></section><p id=out>o</p>");
        doc.GetElementById("a")!.ClassName = "x";
        Assert.That(Color(styles, doc, "b"), Is.EqualTo(Red));
        Assert.That(Color(styles, doc, "c1"), Is.EqualTo(Blue));
        doc.GetElementById("a")!.ClassName = "";
        Assert.That(Restyled(doc, styles), Is.EqualTo(5), "section and its four descendants only");
        Assert.That(Color(styles, doc, "b"), Is.Not.EqualTo(Red));
        Assert.That(Color(styles, doc, "c1"), Is.Not.EqualTo(Blue));
    }

    [Test]
    public void NthChildOfSelectorFollowsSiblingClassChanges()
    {
        var (doc, styles) = Load(":nth-child(2 of .k) { color: red }",
            "<ul><li id=a>a</li><li id=b class=k>b</li><li id=c class=k>c</li></ul>");
        Assert.That(Color(styles, doc, "c"), Is.EqualTo(Red));
        doc.GetElementById("a")!.ClassName = "k";
        Assert.That(Color(styles, doc, "b"), Is.EqualTo(Red));
        Assert.That(Color(styles, doc, "c"), Is.Not.EqualTo(Red));
    }

    [Test]
    public void HoverRestylesTheOutermostMatchingAncestorSubtreeOnly()
    {
        var (doc, styles) = Load(".row:hover { color: green } button:hover { background-color: blue }",
            "<div id=row class=row><span id=label>l</span><button id=btn><b id=glyph>g</b></button></div>"
            + "<div id=other class=row><span id=other1>o</span></div>");
        var glyph = doc.GetElementById("glyph")!;
        glyph.PseudoState |= ElementPseudoState.Hover;
        Assert.That(Restyled(doc, styles), Is.EqualTo(4), "row, label, button and glyph");
        Assert.That(Color(styles, doc, "row"), Is.EqualTo(Green));
        Assert.That(Color(styles, doc, "label"), Is.EqualTo(Green), "inherits from the hovered row");
        Assert.That(Background(styles, doc, "btn"), Is.EqualTo(Blue));
        Assert.That(Color(styles, doc, "other1"), Is.Not.EqualTo(Green));
        glyph.PseudoState &= ~ElementPseudoState.Hover;
        Assert.That(Color(styles, doc, "label"), Is.Not.EqualTo(Green));
        Assert.That(Background(styles, doc, "btn"), Is.Not.EqualTo(Blue));
    }

    [Test]
    public void HoverInAnAncestorCompoundRestylesItsDescendants()
    {
        var (doc, styles) = Load(".menu:hover .item { color: red }",
            "<div id=menu class=menu><p id=p><span id=hot>h</span></p><ul><li id=item class=item>i</li></ul></div>");
        doc.GetElementById("hot")!.PseudoState |= ElementPseudoState.Hover;
        Assert.That(Color(styles, doc, "item"), Is.EqualTo(Red));
        doc.GetElementById("hot")!.PseudoState &= ~ElementPseudoState.Hover;
        Assert.That(Color(styles, doc, "item"), Is.Not.EqualTo(Red));
    }

    [Test]
    public void FocusWithinFollowsFocusMoves()
    {
        var (doc, styles) = Load(".card:focus-within { color: blue }",
            "<div id=c1 class=card><button id=b1>1</button></div><div id=c2 class=card><button id=b2>2</button></div>");
        doc.GetElementById("b1")!.PseudoState |= ElementPseudoState.Focus;
        Assert.That(Color(styles, doc, "c1"), Is.EqualTo(Blue));
        doc.GetElementById("b1")!.PseudoState &= ~ElementPseudoState.Focus;
        doc.GetElementById("b2")!.PseudoState |= ElementPseudoState.Focus;
        Assert.That(Color(styles, doc, "c1"), Is.Not.EqualTo(Blue));
        Assert.That(Color(styles, doc, "c2"), Is.EqualTo(Blue));
    }

    [Test]
    public void RemovingTheHoveredElementClearsAncestorHover()
    {
        var (doc, styles) = Load(".row:hover { color: green }", "<div id=row class=row><span id=label>l</span><i id=hot>h</i></div>");
        var hot = doc.GetElementById("hot")!;
        hot.PseudoState |= ElementPseudoState.Hover;
        Assert.That(Color(styles, doc, "label"), Is.EqualTo(Green));
        hot.Remove();
        Assert.That(Color(styles, doc, "label"), Is.Not.EqualTo(Green));
    }

    [Test]
    public void HoverClearedThenRemovedBeforeTheNextReadStillRestylesAncestors()
    {
        var (doc, styles) = Load(".row:hover { color: green }", "<div id=row class=row><span id=label>l</span><i id=hot>h</i></div>");
        var hot = doc.GetElementById("hot")!;
        hot.PseudoState |= ElementPseudoState.Hover;
        Assert.That(Color(styles, doc, "label"), Is.EqualTo(Green));
        hot.PseudoState &= ~ElementPseudoState.Hover;
        hot.Remove();
        Assert.That(Color(styles, doc, "label"), Is.Not.EqualTo(Green));
    }

    [Test]
    public void StructuralPseudoClassesFollowInsertionAndRemoval()
    {
        var (doc, styles) = Load("li:first-child { color: red } ul:empty { color: blue }",
            "<ul id=list><li id=a>a</li></ul><ul id=empty></ul>");
        var first = doc.CreateElement("li");
        first.Id = "z";
        doc.GetElementById("list")!.InsertBefore(first, doc.GetElementById("a"));
        Assert.That(Color(styles, doc, "z"), Is.EqualTo(Red));
        Assert.That(Color(styles, doc, "a"), Is.Not.EqualTo(Red));
        Assert.That(Color(styles, doc, "empty"), Is.EqualTo(Blue));
        doc.GetElementById("empty")!.AppendChild(doc.CreateTextNode("t"));
        Assert.That(Color(styles, doc, "empty"), Is.Not.EqualTo(Blue));
        first.Remove();
        Assert.That(Color(styles, doc, "a"), Is.EqualTo(Red));
    }

    [Test]
    public void TextChangeUpdatesEmpty()
    {
        var (doc, styles) = Load("p:empty { color: blue }", "<p id=p></p>");
        var text = doc.CreateTextNode("");
        doc.GetElementById("p")!.AppendChild(text);
        Assert.That(Color(styles, doc, "p"), Is.EqualTo(Blue), "a zero-length text node leaves :empty matching");
        text.Data = "now";
        Assert.That(Color(styles, doc, "p"), Is.Not.EqualTo(Blue));
    }

    [Test]
    public void ReinsertedSubtreeTakesItsNewAncestorsValues()
    {
        var (doc, styles) = Load("#a { color: red } #b { color: blue }", "<div id=a><span id=s>s</span></div><div id=b></div>");
        Assert.That(Color(styles, doc, "s"), Is.EqualTo(Red));
        doc.GetElementById("b")!.AppendChild(doc.GetElementById("s")!);
        Assert.That(Color(styles, doc, "s"), Is.EqualTo(Blue));
    }

    [Test]
    public void DirAutoFollowsDescendantText()
    {
        var (doc, styles) = Load("div:dir(rtl) span { color: red }", "<div id=d dir=auto><p id=p></p><span id=s>s</span></div>");
        Assert.That(Color(styles, doc, "s"), Is.Not.EqualTo(Red));
        doc.GetElementById("p")!.AppendChild(doc.CreateTextNode("\u05D0"));
        Assert.That(Color(styles, doc, "s"), Is.EqualTo(Red));
    }

    [Test]
    public void OptionSelectednessFollowsSiblingSelectedAttribute()
    {
        var (doc, styles) = Load("option:checked { color: red }",
            "<select><option id=a>a</option><option id=b>b</option></select>");
        Assert.That(Color(styles, doc, "a"), Is.EqualTo(Red));
        doc.GetElementById("b")!.SetAttribute("selected", "");
        Assert.That(Color(styles, doc, "a"), Is.Not.EqualTo(Red));
        Assert.That(Color(styles, doc, "b"), Is.EqualTo(Red));
    }

    [Test]
    public void HasSelectorsRestyleTheDocument()
    {
        var (doc, styles) = Load("div:has(.on) { color: red }", "<div id=d><p><span id=s>s</span></p></div>");
        doc.GetElementById("s")!.ClassName = "on";
        Assert.That(Color(styles, doc, "d"), Is.EqualTo(Red));
    }

    [Test]
    public void StyleElementTextChangeRestylesTheDocument()
    {
        var (doc, styles) = Load("p { color: red }", "<p id=p>p</p>");
        doc.QuerySelector("style")!.TextContent = "p { color: blue }";
        Assert.That(Color(styles, doc, "p"), Is.EqualTo(Blue));
    }

    [Test]
    public void InsertedStyleSheetRestylesTheDocument()
    {
        var (doc, styles) = Load("", "<p id=p>p</p>");
        var style = doc.CreateElement("style");
        style.TextContent = "p { color: green }";
        doc.Body!.AppendChild(style);
        Assert.That(Color(styles, doc, "p"), Is.EqualTo(Green));
    }
}
