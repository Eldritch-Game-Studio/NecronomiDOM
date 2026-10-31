using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class RuntimeStyleProfileTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void UnchangedStylesheetsValidateWithoutWarmAllocation(bool inline, bool split)
    {
        // Reconciling unchanged stylesheets must not recascade or allocate.
        const string css = "div:is(:first-child, :nth-child(2 of div)){color:red}";
        var doc = HtmlImporter.Import((inline ? "<style>" + css + "</style>" : "") + "<div id=x></div>",
            "profile", WebUrl.Parse("res:///profile.html")!).Document;
        if (!inline) doc.AdoptedStyleSheets.Add(CSSStyleSheet.Parse(css));
        if (split)
        {
            var style = doc.QuerySelector("style")!; style.TextContent = "";
            style.AppendChild(doc.CreateTextNode(css[..20]));
            style.AppendChild(doc.CreateTextNode(css[20..]));
        }
        for (int i = 0; i < 4; i++) RuntimeStyleProfile.Validate(doc);
        long allocated = AllocationWindow.Measure(100, _ => RuntimeStyleProfile.Validate(doc));
        Assert.That(allocated, Is.Zero);
    }

    [TestCase("div:visited")]
    [TestCase("div:is(:visited)")]
    [TestCase("div:nth-child(2 of :visited)")]
    [TestCase("div::before")]
    public void SelectorCssomMutationCannotReuseAnEarlierValidation(string selector)
    {
        var doc = HtmlImporter.Import("<div></div>", "profile", WebUrl.Parse("res:///profile.html")!).Document;
        var sheet = CSSStyleSheet.Parse("div{color:red}"); doc.AdoptedStyleSheets.Add(sheet);
        RuntimeStyleProfile.Validate(doc);
        var rule = (CSSStyleRule)sheet.CssRules[0];
        rule.SelectorText = selector;
        Assert.Throws<NotSupportedException>(() => RuntimeStyleProfile.Validate(doc));
        Assert.Throws<NotSupportedException>(() => RuntimeStyleProfile.Validate(doc), "failed validation is never cached as valid");
        rule.SelectorText = "div";
        Assert.DoesNotThrow(() => RuntimeStyleProfile.Validate(doc));
    }

    [Test]
    public void NewMembershipAndInlineStyleTextStillValidateTheRealNewSheet()
    {
        var doc = HtmlImporter.Import("<style>div{color:red}</style><div></div>", "profile", WebUrl.Parse("res:///profile.html")!).Document;
        RuntimeStyleProfile.Validate(doc);
        doc.QuerySelector("style")!.TextContent = "div:visited{color:red}";
        Assert.Throws<NotSupportedException>(() => RuntimeStyleProfile.Validate(doc));
        doc.QuerySelector("style")!.TextContent = "div{color:blue}";
        RuntimeStyleProfile.Validate(doc);
        doc.AdoptedStyleSheets.Add(CSSStyleSheet.Parse("@supports(display:block){div{color:red}}"));
        Assert.Throws<NotSupportedException>(() => RuntimeStyleProfile.Validate(doc));
    }

    [Test]
    public void FragmentedInlineTextMutationIsNotHiddenByTheSheetTextFastPath()
    {
        var doc = HtmlImporter.Import("<style></style><div></div>", "profile", WebUrl.Parse("res:///profile.html")!).Document;
        var style = doc.QuerySelector("style")!;
        style.AppendChild(doc.CreateTextNode("div"));
        style.AppendChild(doc.CreateTextNode("{color:red}"));
        RuntimeStyleProfile.Validate(doc);
        style.FirstChild!.NodeValue = "div:visited";
        Assert.Throws<NotSupportedException>(() => RuntimeStyleProfile.Validate(doc));
    }

    [Test]
    public void EmptyInlineStylesheetValidationDoesNotAllocateWhenUnchanged()
    {
        var doc = HtmlImporter.Import("<style></style>", "profile", WebUrl.Parse("res:///profile.html")!).Document;
        for (int i = 0; i < 4; i++) RuntimeStyleProfile.Validate(doc);
        long allocated = AllocationWindow.Measure(100, _ => RuntimeStyleProfile.Validate(doc));
        Assert.That(allocated, Is.Zero);
    }
}
