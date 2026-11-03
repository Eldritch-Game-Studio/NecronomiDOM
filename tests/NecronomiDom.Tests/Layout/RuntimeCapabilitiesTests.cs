using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Layout;

[TestFixture]
public sealed class RuntimeCapabilitiesTests
{
    private sealed class Metrics : ITextShaper
    {
        public TextMetrics Measure(string text, int fontSize, double fontWeight = 400,
            double letterSpacing = 0, bool italic = false) => new(text.Length * 8, 12, 4);
    }

    private static (Document Document, FlowLayout Layout) Fixture(string element, string css)
    {
        var document = HtmlImporter.Import("<!doctype html><style>" +
            "html,body,#target{display:block;margin:0;padding:0;border:0 solid black;box-sizing:content-box;" +
            "font-family:monospace;font-size:16px;line-height:20px;appearance:none}" +
            css + "</style><" + element + " id=target></" + element + ">",
            "capabilities", WebUrl.Parse("res:///capabilities.html")!).Document;
        return (document, new FlowLayout(new StyleEngine(document), new Metrics(), "monospace"));
    }

    [Test]
    public void EveryAdmittedPropertyIsAnActualParserPropertyWithoutDuplicates()
    {
        Assert.That(RuntimeCapabilities.Properties, Is.Unique);
        foreach (string property in RuntimeCapabilities.Properties)
            Assert.That(CssPropertyRegistry.Get(property), Is.Not.Null, property);
        Assert.That(RuntimeCapabilities.InlineElements, Is.SubsetOf(RuntimeCapabilities.Elements));
        Assert.That(RuntimeCapabilities.Properties, Does.Not.Contain("filter"));
        Assert.That(CssPropertyRegistry.Get("filter"), Is.Not.Null, "syntax support must not imply renderer support");
    }

    [TestCase("display", "grid")]
    [TestCase("filter", "blur(2px)")]
    [TestCase("text-align", "justify")]
    [TestCase("font-style", "oblique")]
    [TestCase("cursor", "help")]
    public void ParserAcceptedDeclarationsStillFailTheRealRenderer(string property, string value)
    {
        string css = "#target{" + property + ":" + value + "}";
        var rule = (CSSStyleRule)CSSStyleSheet.Parse(css).CssRules[0];
        Assert.That(rule.Style.GetPropertyValue(property), Is.Not.Empty, "exercise accepted syntax, not parse rejection");
        var (document, layout) = Fixture("div", css);
        Assert.Throws<NotSupportedException>(() => layout.Layout(document, 320, 240));
    }

    [Test]
    public void HtmlDomRepresentationDoesNotImplyRenderedElementSupport()
    {
        var (document, layout) = Fixture("article", "");
        Assert.That(document.GetElementById("target"), Is.TypeOf<HTMLElement>());
        Assert.That(RuntimeCapabilities.AdmitsElement("article"), Is.False);
        Assert.Throws<NotSupportedException>(() => layout.Layout(document, 320, 240));
    }

    [TestCase("div:visited{color:red}")]
    [TestCase("div::before{color:red}")]
    [TestCase("div:is(:visited){color:red}")]
    [TestCase("@supports(display:block){div{color:red}}")]
    public void ParserAcceptedSelectorsAndRulesUseRuntimeAdmission(string css)
    {
        var sheet = CSSStyleSheet.Parse(css);
        Assert.That(sheet.CssRules.Length, Is.GreaterThan(0));
        var (document, _) = Fixture("div", "");
        document.AdoptedStyleSheets.Add(sheet);
        Assert.Throws<NotSupportedException>(() => RuntimeStyleProfile.Validate(document));
    }

    [Test]
    public void ExposedCapabilityCollectionsCannotMutateValidation()
    {
        var names = (IList<string>)RuntimeCapabilities.Elements;
        Assert.Throws<NotSupportedException>(() => names.Add("article"));
        var keywords = (IList<string>)RuntimeCapabilities.AllowedKeywords("cursor");
        Assert.Throws<NotSupportedException>(() => keywords[0] = "help");
        Assert.That(RuntimeCapabilities.AdmitsKeyword("cursor", "help"), Is.False);
        Assert.That(RuntimeCapabilities.AdmitsKeyword("unknown-property", "anything"), Is.False);
    }
}
