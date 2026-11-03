using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Cascade;

[TestFixture]
public sealed class StyleInspectionTests
{
    [Test]
    public void InspectionUsesRealLayerReversionImportantOrderAndSourceOwnership()
    {
        var document = CreateDocument();
        var sheet = CSSStyleSheet.Parse("@layer base, theme; @layer base { div { width:10px; opacity:.25!important; } } @layer theme { div { width:revert-layer; opacity:.8!important; } }", "theme.css");
        document.AdoptedStyleSheets.Add(sheet);
        var element = document.CreateElement("div"); document.Body!.AppendChild(element);
        var engine = new StyleEngine(document);
        var snapshot = engine.Inspect(element);
        var width = snapshot.Declarations.Single(item => item.Property == "width" && item.Winner);
        var opacity = snapshot.Declarations.Single(item => item.Property == "opacity" && item.Winner);
        Assert.Multiple(() =>
        {
            Assert.That(width.Value, Is.EqualTo("10px"));
            Assert.That(opacity.Value, Is.EqualTo("0.25"));
            Assert.That(opacity.Priority.Important, Is.True);
            Assert.That(snapshot.Declarations.Single(item => item.Property == "width" && item.Value == "revert-layer").Winner, Is.False);
            Assert.That(snapshot.Sources.Single(item => item.Order == width.SourceOrder).SourcePath, Is.EqualTo("theme.css"));
            Assert.That(snapshot.Sources.Single(item => item.Order == width.SourceOrder).Selector, Is.EqualTo("div"));
            Assert.That(snapshot.Computed.Single(item => item.Name == "width").Value, Is.EqualTo("10px"));
        });
    }

    [Test]
    public void InlineLogicalWinnerAndComputedSnapshotsRemainIndependentOfLaterChanges()
    {
        var document = CreateDocument();
        var element = (HTMLElement)document.CreateElement("div"); document.Body!.AppendChild(element);
        element.Style.CssText = "direction:rtl; margin-inline-start:12px; width:20px;";
        var engine = new StyleEngine(document);
        var before = engine.Inspect(element);
        element.Style.SetProperty("width", "35px");
        var after = engine.Inspect(element);
        var margin = before.Declarations.Single(item => item.Property == "margin-right" && item.Winner);
        Assert.Multiple(() =>
        {
            Assert.That(margin.DeclaredProperty, Is.EqualTo("margin-inline-start"));
            Assert.That(margin.Priority.Inline, Is.True);
            Assert.That(before.Sources.Single(item => item.Order == margin.SourceOrder).Inline, Is.True);
            Assert.That(before.Computed.Single(item => item.Name == "width").Value, Is.EqualTo("20px"));
            Assert.That(after.Computed.Single(item => item.Name == "width").Value, Is.EqualTo("35px"));
        });
        Assert.Throws<NotSupportedException>(() => ((IList<InspectedProperty>)before.Computed).Clear());
    }

    [Test]
    public void CustomInspectionIncludesInheritedResolvedCyclicMissingAndFallbackConsumerValues()
    {
        var document = CreateDocument();
        document.AdoptedStyleSheets.Add(CSSStyleSheet.Parse("body { --base:13px; } div { --alias:var(--base); --cycle:var(--cycle); width:var(--alias); height:var(--missing,7px); }"));
        var element = document.CreateElement("div"); document.Body!.AppendChild(element);
        var snapshot = new StyleEngine(document).Inspect(element);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CustomProperties.Single(item => item.Name == "--base").LocallyDeclared, Is.False);
            Assert.That(snapshot.CustomProperties.Single(item => item.Name == "--base").Resolved!.Trim(), Is.EqualTo("13px"));
            Assert.That(snapshot.CustomProperties.Single(item => item.Name == "--alias").Resolved!.Trim(), Is.EqualTo("13px"));
            Assert.That(snapshot.CustomProperties.Single(item => item.Name == "--cycle").Resolved, Is.Null);
            Assert.That(snapshot.CustomProperties.Single(item => item.Name == "--missing").Resolved, Is.Null);
            Assert.That(snapshot.Computed.Single(item => item.Name == "width").Value, Is.EqualTo("13px"));
            Assert.That(snapshot.Computed.Single(item => item.Name == "height").Value, Is.EqualTo("7px"));
        });
    }

    [Test]
    public void MatchedRulesRespectMediaSelectorsDisabledSheetsAndLiveCssomEdits()
    {
        var document = CreateDocument();
        var sheet = CSSStyleSheet.Parse("@media (min-width:900px) { .target { width:99px; } } .other { height:88px; } .target { width:22px; }");
        document.AdoptedStyleSheets.Add(sheet);
        var element = document.CreateElement("div"); element.SetAttribute("class", "target"); document.Body!.AppendChild(element);
        var engine = new StyleEngine(document); engine.UpdateViewport(600, 400);
        var before = engine.Inspect(element);
        Assert.That(before.Declarations.Where(item => item.Property == "width").Select(item => item.Value), Is.EquivalentTo(new[] { "22px" }));
        Assert.That(before.Declarations.Any(item => item.Property == "height" && item.Value == "88px"), Is.False);
        ((CSSStyleRule)sheet.CssRules[2]).Style.SetProperty("width", "31px");
        Assert.That(engine.Inspect(element).Declarations.Single(item => item.Property == "width" && item.Winner).Value, Is.EqualTo("31px"));
        sheet.Disabled = true;
        Assert.That(engine.Inspect(element).Sources.Any(item => item.RuleText.Contains("31px", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void ForeignAndDetachedElementsAreRejectedInsteadOfReportingFalseMatches()
    {
        var document = CreateDocument();
        var engine = new StyleEngine(document);
        Assert.Throws<ArgumentException>(() => engine.Inspect(CreateDocument().Body!));
        Assert.Throws<ArgumentException>(() => engine.Inspect(document.CreateElement("div")));
    }

    private static Document CreateDocument() => new Document().Implementation.CreateHTMLDocument("inspection");
}
