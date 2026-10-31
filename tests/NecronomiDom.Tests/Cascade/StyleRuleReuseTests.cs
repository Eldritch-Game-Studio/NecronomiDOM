using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class StyleRuleReuseTests
{
    private static (Document Doc, StyleEngine Styles) Load(string css = "")
    {
        var doc = HtmlImporter.Import("<!doctype html><style>html{--base:red}#p{--paint:var(--base)}" +
            ".polygon{width:100px;background-color:var(--paint)}.blue{--paint:blue}" + css +
            "</style><div id=p style='--paint:red'><div id=c class=polygon style='clip-path:polygon(0px 0px,20px 0px,20px 20px)'></div></div>",
            "rule-reuse", WebUrl.Parse("res:///rule-reuse.html")!).Document;
        return (doc, new StyleEngine(doc));
    }
    private static void Compare(Document a, StyleEngine actual, Document b, StyleEngine reference)
    {
        foreach (var node in a.QuerySelectorAll("[id]"))
        {
            var element = (Element)node;
            var left = actual.GetComputedStyle(element);
            var right = reference.GetComputedStyle(b.GetElementById(element.Id)!);
            Assert.That(left.Length, Is.EqualTo(right.Length));
            for (int i = 0; i < right.Length; i++)
            {
                string name = right.Item(i);
                Assert.That(left.Item(i), Is.EqualTo(name), element.Id + ":CSSOM order");
                Assert.That(left.GetPropertyValue(name), Is.EqualTo(right.GetPropertyValue(name)), element.Id + ":" + name);
            }
        }
    }
    [Test]
    public void CustomInheritanceInlineMembershipPriorityAndGeometryMatchFullCascade()
    {
        var (doc, styles) = Load(); var (control, reference) = Load();
        Compare(doc, styles, control, reference);
        for (int i = 0; i < 8; i++)
        {
            foreach (var document in new[] { doc, control })
            {
                var parent = (HTMLElement)document.GetElementById("p")!;
                var child = (HTMLElement)document.GetElementById("c")!;
                parent.Style.SetProperty("--paint", i % 2 == 0 ? "green" : "blue", i % 3 == 0 ? "important" : "");
                child.Style.SetProperty("clip-path", $"polygon(0px 0px,{20 + i}px 0px,20px 20px)");
                if (i % 2 == 0) child.Style.SetProperty("height", "30px"); else child.Style.RemoveProperty("height");
            }
            reference.Invalidate(); Compare(doc, styles, control, reference);
        }
        Assert.That(styles.ReusedRuleCollectionCount, Is.GreaterThan(0));
    }
    [TestCase("[style*='green']{width:12px}")]
    [TestCase("#p{width:attr(data-width px)}")]
    public void StyleAttributeDependenciesDoNotReuseRules(string css)
    {
        var (doc, styles) = Load(css); var (control, reference) = Load(css);
        Compare(doc, styles, control, reference);
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("--paint", "green");
        ((HTMLElement)control.GetElementById("p")!).Style.SetProperty("--paint", "green");
        reference.Invalidate(); Compare(doc, styles, control, reference);
        Assert.That(styles.ReusedRuleCollectionCount, Is.Zero);
    }
    [Test]
    public void BroaderInvalidationAndSheetRevisionCannotReuseStaleMatches()
    {
        var (doc, styles) = Load(); var (control, reference) = Load();
        Compare(doc, styles, control, reference);
        foreach (var document in new[] { doc, control })
        {
            document.GetElementById("p")!.ClassName = "blue";
            ((HTMLElement)document.GetElementById("c")!).Style.SetProperty("height", "30px");
        }
        reference.Invalidate(); Compare(doc, styles, control, reference);
        Assert.That(styles.ReusedRuleCollectionCount, Is.Zero);
        doc.StyleSheets[0].InsertRule("#c{background-color:yellow!important}");
        control.StyleSheets[0].InsertRule("#c{background-color:yellow!important}");
        Compare(doc, styles, control, reference);
    }
    [Test]
    public void InlineChangesDoNotRestyleUnrelatedSiblingsButStillReachInheritedChildren()
    {
        const string html = "<!doctype html><style>.item+.item{padding:2px}#p{--paint:red}" +
            "#c{background-color:var(--paint)}</style><div id=p class=item><div id=c></div></div>" +
            "<div id=other class=item></div>";
        var doc = HtmlImporter.Import(html, "inline-scope", WebUrl.Parse("res:///inline-scope.html")!).Document;
        var control = HtmlImporter.Import(html, "inline-scope", WebUrl.Parse("res:///inline-scope.html")!).Document;
        var styles = new StyleEngine(doc); var reference = new StyleEngine(control) { IncrementalReconciliation = false };
        Compare(doc, styles, control, reference);
        long computed = styles.ComputedEntryCount;
        ((HTMLElement)doc.GetElementById("p")!).Style.SetProperty("--paint", "green");
        ((HTMLElement)control.GetElementById("p")!).Style.SetProperty("--paint", "green");
        _ = styles.GetComputedStyle(doc.GetElementById("other")!).GetPropertyValue("padding");
        Assert.That(styles.ComputedEntryCount, Is.EqualTo(computed));
        Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedStyle(doc.GetElementById("c")!).GetPropertyValue("background-color"),
            Is.EqualTo(reference.GetComputedStyle(control.GetElementById("c")!).GetPropertyValue("background-color")));
        Assert.That(styles.ComputedEntryCount - computed, Is.EqualTo(2));
    }
    private static (Document Doc, StyleEngine Styles) LoadHtml(string html, bool incremental)
    {
        var doc = HtmlImporter.Import(html, "substitution-reuse", WebUrl.Parse("res:///substitution-reuse.html")!).Document;
        return (doc, new StyleEngine(doc) { IncrementalReconciliation = incremental });
    }
    // CSS Variables 1 #using-variables and CSS Values 4 #font-relative-lengths:
    // only syntax is shared; computed font/currentcolor context remains local to the element.
    [Test]
    public void SharedSubstitutionSyntaxKeepsRelativeUnitsAndCurrentColorElementLocal()
    {
        const string html = "<!doctype html><style>html{--size:2em;--paint:currentcolor;--bad:invalid}" +
            "div{width:var(--size);height:var(--size);background-color:var(--paint);border-top-width:var(--bad)}" +
            "#a{font-size:10px;color:red}#b{font-size:20px;color:blue}</style><div id=a></div><div id=b></div>";
        var (doc, styles) = LoadHtml(html, true); var (control, reference) = LoadHtml(html, false);
        _ = styles.GetComputedStyle(doc.GetElementById("a")!).GetPropertyValue("width");
        long parses = styles.SubstitutionParseCount;
        Assert.That(parses, Is.GreaterThan(0));
        _ = styles.GetComputedStyle(doc.GetElementById("b")!).GetPropertyValue("width");
        Assert.That(styles.SubstitutionParseCount, Is.EqualTo(parses), "Inherited immutable syntax includes cached invalid declarations.");
        Compare(doc, styles, control, reference);
        Assert.That(styles.GetComputedStyle(doc.GetElementById("a")!).GetPropertyValue("width"), Is.EqualTo("20px"));
        Assert.That(styles.GetComputedStyle(doc.GetElementById("b")!).GetPropertyValue("width"), Is.EqualTo("40px"));
        foreach (var document in new[] { doc, control })
        {
            ((HTMLElement)document.GetElementById("a")!).Style.SetProperty("font-size", "30px");
            ((HTMLElement)document.GetElementById("b")!).Style.SetProperty("color", "green");
        }
        reference.Invalidate(); Compare(doc, styles, control, reference);
        Assert.That(styles.SubstitutionParseCount, Is.EqualTo(parses));
        Assert.That(styles.GetComputedStyle(doc.GetElementById("a")!).GetPropertyValue("width"), Is.EqualTo("60px"));
    }
    // The property name is part of the parse key; invalid-at-computed-value results are cached too.
    [Test]
    public void SubstitutionCacheSeparatesPropertiesAndChangingCustomTokenIdentity()
    {
        const string html = "<!doctype html><style>html{--value:red}div{color:var(--value);width:var(--value)}</style>" +
            "<div id=a></div><div id=b></div>";
        var (doc, styles) = LoadHtml(html, true); var (control, reference) = LoadHtml(html, false);
        foreach (string value in new[] { "red", "15px", "green", "invalid" })
        {
            foreach (var document in new[] { doc, control })
                ((HTMLElement)document.DocumentElement!).Style.SetProperty("--value", value);
            reference.Invalidate();
            _ = styles.GetComputedStyle(doc.GetElementById("a")!).GetPropertyValue("width");
            long parses = styles.SubstitutionParseCount;
            Compare(doc, styles, control, reference);
            Assert.That(styles.SubstitutionParseCount, Is.EqualTo(parses), value);
        }
    }
    // CSS Values 5 attr-taint URL security (see CssAttrParserTests) must survive cached rule reuse.
    [Test]
    public void CachedUrlSyntaxCannotBypassAttributeTaint()
    {
        const string html = "<!doctype html><style>html{--picture:url('res:///safe.png')}" +
            "div{background-image:var(--picture)}#a{--picture:attr(data-picture type(*))}</style>" +
            "<div id=b></div><div id=a data-picture=\"url('res:///safe.png')\"></div>";
        var (doc, styles) = LoadHtml(html, true); var (control, reference) = LoadHtml(html, false);
        Assert.That(styles.GetComputedStyle(doc.GetElementById("b")!).GetPropertyValue("background-image"), Is.Not.EqualTo("none"));
        foreach (string value in new[] { "url('res:///safe.png')", "none", "url('res:///other.png')" })
        {
            foreach (var document in new[] { doc, control }) document.GetElementById("a")!.SetAttribute("data-picture", value);
            reference.Invalidate(); Compare(doc, styles, control, reference);
            Assert.That(styles.GetComputedStyle(doc.GetElementById("a")!).GetPropertyValue("background-image"), Is.EqualTo("none"));
        }
    }
    // Full-cascade/Selectors references: unrelated text changes cannot make inline styles selector-visible;
    // a genuine overlapping class/DOM invalidation must still discard old matches in either mutation order.
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void MixedInlineAndDomInvalidationKeepsIndependentScopeButOrdinaryOverlapDominates(bool overlap, bool inlineFirst)
    {
        const string html = "<!doctype html><style>.item+.item{padding:2px}#p{--paint:red}" +
            "#c{background-color:var(--paint)}#p.changed #c{width:42px}</style>" +
            "<main id=paint-scope><div id=p class=item><div id=c></div></div><div id=other class=item></div></main>" +
            "<aside id=text-scope><p id=label>before</p></aside>";
        var (doc, styles) = LoadHtml(html, true); var (control, reference) = LoadHtml(html, false);
        Compare(doc, styles, control, reference);
        long computed = styles.ComputedEntryCount, reused = styles.ReusedRuleCollectionCount;
        foreach (var document in new[] { doc, control })
        {
            void Inline() => ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("--paint", "green");
            void Ordinary()
            {
                if (overlap) document.GetElementById("p")!.ClassName = "item changed";
                else document.GetElementById("label")!.TextContent = "after";
            }
            if (inlineFirst) { Inline(); Ordinary(); } else { Ordinary(); Inline(); }
        }
        _ = styles.GetComputedStyle(doc.GetElementById("other")!).GetPropertyValue("padding");
        if (!overlap) Assert.That(styles.ComputedEntryCount, Is.EqualTo(computed), "Unrelated sibling was not invalidated.");
        reference.Invalidate(); Compare(doc, styles, control, reference);
        if (overlap) Assert.That(styles.ReusedRuleCollectionCount, Is.EqualTo(reused), "Ordinary invalidation dominates reused matches.");
        else Assert.That(styles.ReusedRuleCollectionCount, Is.GreaterThan(reused));
    }
    [Test]
    public void ComputedProjectionResortsWhenOneCustomNameReplacesAnotherAtEqualCount()
    {
        const string html = "<!doctype html><style>html{--paint:red}#p{background-color:var(--paint)}</style><div id=p></div>";
        var (doc, styles) = LoadHtml(html, true); var (control, reference) = LoadHtml(html, false);
        foreach (var document in new[] { doc, control }) ((HTMLElement)document.DocumentElement!).Style.SetProperty("--aaa", "10px");
        Compare(doc, styles, control, reference);
        var typed = styles.GetComputedValues(doc.GetElementById("p")!);
        foreach (var document in new[] { doc, control })
        {
            var root = (HTMLElement)document.DocumentElement!;
            root.Style.RemoveProperty("--aaa"); root.Style.SetProperty("--zzz", "20px");
        }
        reference.Invalidate(); Compare(doc, styles, control, reference);
        var expected = reference.GetComputedValues(control.GetElementById("p")!);
        Assert.That(typed.Length, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(typed.Item(i), Is.EqualTo(expected.Item(i)));
            Assert.That(typed.GetPropertyValue(expected.Item(i)), Is.EqualTo(expected.GetPropertyValue(expected.Item(i))));
        }
    }
    [Test]
    public void CachedPhysicalNamesRemainDirectionSpecificAcrossRepeatedDirectionChanges()
    {
        const string html = "<!doctype html><div id=p style='margin-inline-start:10px;margin-inline-end:20px'></div>";
        var (doc, styles) = LoadHtml(html, true); var (control, reference) = LoadHtml(html, false);
        foreach (string direction in new[] { "ltr", "rtl", "ltr" })
        {
            foreach (var document in new[] { doc, control })
                ((HTMLElement)document.GetElementById("p")!).Style.SetProperty("direction", direction);
            reference.Invalidate(); Compare(doc, styles, control, reference);
            var actual = styles.GetComputedStyle(doc.GetElementById("p")!);
            Assert.That(actual.GetPropertyValue("margin-left"), Is.EqualTo(direction == "rtl" ? "20px" : "10px"));
            Assert.That(actual.GetPropertyValue("margin-right"), Is.EqualTo(direction == "rtl" ? "10px" : "20px"));
        }
    }
}
