using System;
using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NUnit.Framework;

namespace NecronomiDom.Tests.Html;

[TestFixture]
public sealed class DocumentFileReloadTests
{
    private const string Path = "res://ui/page.html";
    private const string CssPath = "res://ui/css/theme.css";
    private const string Markup = "<!doctype html><link rel=stylesheet href=css/theme.css><input id=field value=authored><button id=action>Go</button><div id=scroll><div id=content></div></div>";
    private const string Css = "*{font-family:monospace;font-size:16px;line-height:16px;appearance:none}html,body,input,button,div{display:block;margin:0;padding:0;border:0 solid black;box-sizing:border-box}#action{width:40px}#scroll{width:100px;height:60px;overflow:auto}#content{height:400px}";
    private static Dictionary<string, string> Files() => new(StringComparer.Ordinal) { [Path] = Markup, [CssPath] = Css };
    private sealed class Metrics : ITextShaper
    {
        public TextMetrics Measure(string text, int size, double weight = 400, double spacing = 0, bool italic = false) => new(text.Length * 8, 12, 4);
    }

    [TestCase("display:grid")]
    [TestCase("--layout:grid;display:var(--layout)")]
    [TestCase("position:fixed")]
    [TestCase("filter:blur(2px)")]
    public void RendererPreflightRejectsComputedProfileErrorsWithoutPublishingColorGeometryOrState(string invalid)
    {
        var files = Files(); files[CssPath] = Css + "#action{color:red;background-color:blue}";
        var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        var doc = loaded.Document;
        var styles = new StyleEngine(doc);
        var layout = new FlowLayout(styles, new Metrics(), "monospace");
        var display = layout.Layout(doc, 400, 300);
        using var input = new DocumentInput(doc, styles);
        input.UpdateGeometry(display, 400, 300);
        var field = (HTMLInputElement)doc.GetElementById("field")!;
        field.Value = "game value"; field.SetSelectionRange(1, 4);
        field.Focus(new FocusOptions { PreventScroll = true });
        var scroll = doc.GetElementById("scroll")!; scroll.ScrollTop = 37;
        var button = (HTMLElement)doc.GetElementById("action")!;
        var bounds = display.Boxes.Single(b => ReferenceEquals(b.Element, button)).Bounds;
        var color = styles.GetComputedStyle(button).GetPropertyValue("color");
        var originalSheet = doc.StyleSheets[0];
        int clicks = 0; button.AddEventListener("click", new DelegateEventListener(_ => clicks++));
        files[CssPath] = Css + "#action{color:green;width:120px;" + invalid + "}";
        var update = loaded.PrepareUpdate(p => files[p]);
        Assert.Throws<NotSupportedException>(() => update.ApplyStyles(preview =>
        {
            Assert.That(preview, Is.Not.SameAs(doc));
            Assert.That(((HTMLInputElement)preview.GetElementById("field")!).Value, Is.EqualTo("game value"));
            Assert.That(preview.GetElementById("field")!.PseudoState & ElementPseudoState.Focus, Is.Not.EqualTo(ElementPseudoState.None));
            var previewStyles = new StyleEngine(preview);
            previewStyles.UpdateViewport(400, 300);
            new FlowLayout(previewStyles, new Metrics(), "monospace").Layout(preview, 400, 300);
        }));
        Assert.That(doc.StyleSheets[0], Is.SameAs(originalSheet));
        Assert.That(styles.GetComputedStyle(button).GetPropertyValue("color"), Is.EqualTo(color));
        Assert.That(doc.FocusedElement, Is.SameAs(field));
        Assert.That(field.Value, Is.EqualTo("game value"));
        Assert.That((field.SelectionStart, field.SelectionEnd), Is.EqualTo((1, 4)));
        Assert.That(scroll.ScrollTop, Is.EqualTo(37));
        var after = layout.Layout(doc, 400, 300);
        Assert.That(after.Boxes.Single(b => ReferenceEquals(b.Element, button)).Bounds, Is.EqualTo(bounds));
        button.Click(); Assert.That(clicks, Is.EqualTo(1));
        files[CssPath] = Css + "#action{color:orange;width:80px}";
        loaded.PrepareUpdate(p => files[p]).ApplyStyles(preview =>
            new FlowLayout(new StyleEngine(preview), new Metrics(), "monospace").Layout(preview, 400, 300));
        Assert.That(styles.GetComputedStyle(button).GetPropertyValue("width"), Is.EqualTo("80px"));
        Assert.That(styles.GetComputedStyle(button).GetPropertyValue("color"), Is.EqualTo("rgb(255, 165, 0)"));
    }

    [Test]
    public void CssReloadPreservesDocumentInputFocusScrollValuesListenersAndAdoptedSheets()
    {
        var files = Files();
        var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        var doc = loaded.Document;
        var styles = new StyleEngine(doc);
        var layout = new FlowLayout(styles, new Metrics(), "monospace");
        using var input = new DocumentInput(doc, styles);
        input.UpdateGeometry(layout.Layout(doc, 400, 300), 400, 300);
        var field = (HTMLInputElement)doc.GetElementById("field")!;
        field.Focus(new FocusOptions { PreventScroll = true });
        field.Value = "game value";
        field.SetSelectionRange(1, 4);
        var scroll = doc.GetElementById("scroll")!;
        scroll.ScrollTop = 37;
        int clicks = 0;
        var button = (HTMLElement)doc.GetElementById("action")!;
        button.AddEventListener("click", new DelegateEventListener(_ => clicks++));
        var adopted = new CSSStyleSheet(); adopted.ReplaceSync("#action{height:20px}"); doc.AdoptedStyleSheets.Add(adopted);
        files[CssPath] = Css.Replace("width:40px", "width:80px", StringComparison.Ordinal);
        var update = loaded.PrepareUpdate(p => files[p]);
        Assert.That(update.Replacement, Is.Null);
        Assert.That(styles.GetComputedStyle(button).GetPropertyValue("width"), Is.EqualTo("40px"), "Preparation must not mutate live CSS.");
        update.ApplyStyles();
        input.UpdateGeometry(layout.Layout(doc, 400, 300), 400, 300);
        Assert.That(loaded.Document, Is.SameAs(doc));
        Assert.That(doc.GetElementById("field"), Is.SameAs(field));
        Assert.That(doc.FocusedElement, Is.SameAs(field));
        Assert.That(field.Value, Is.EqualTo("game value"));
        Assert.That((field.SelectionStart, field.SelectionEnd), Is.EqualTo((1, 4)));
        Assert.That(scroll.ScrollTop, Is.EqualTo(37));
        Assert.That(styles.GetComputedStyle(button).GetPropertyValue("width"), Is.EqualTo("80px"));
        Assert.That(doc.AdoptedStyleSheets[0], Is.SameAs(adopted));
        button.Click(); Assert.That(clicks, Is.EqualTo(1));
        Assert.That(loaded.PrepareUpdate(p => files[p]).StylesChanged, Is.False);
    }

    [Test]
    public void MarkupAndForcePrepareNewDocumentsWithoutChangingLiveDocument()
    {
        var files = Files(); var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        var oldField = loaded.Document.GetElementById("field");
        var unchanged = loaded.PrepareUpdate(p => files[p]);
        Assert.That(unchanged.Replacement, Is.Null); Assert.That(unchanged.StylesChanged, Is.False);
        var forced = loaded.PrepareUpdate(p => files[p], true).Replacement!;
        Assert.That(forced.Document, Is.Not.SameAs(loaded.Document));
        files[Path] = Markup.Replace("value=authored", "value=changed", StringComparison.Ordinal);
        var replacement = loaded.PrepareUpdate(p => files[p]).Replacement!;
        Assert.That(replacement.Document.GetElementById("field"), Is.Not.SameAs(oldField));
        Assert.That(((HTMLInputElement)replacement.Document.GetElementById("field")!).Value, Is.EqualTo("changed"));
        Assert.That(loaded.Document.GetElementById("field"), Is.SameAs(oldField));
    }

    [TestCase("@import '../other.css';")]
    [TestCase("button::before{color:red}")]
    [TestCase("@supports(display:block){button{color:red}}")]
    public void UnsupportedCssFailsBeforeAnySheetIsChanged(string unsupported)
    {
        var files = Files(); var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        var original = loaded.Document.StyleSheets[0];
        files[CssPath] = unsupported;
        Assert.Throws<NotSupportedException>(() => loaded.PrepareUpdate(p => files[p]));
        Assert.That(loaded.Document.StyleSheets[0], Is.SameAs(original));
        files[CssPath] = Css;
        Assert.That(loaded.PrepareUpdate(p => files[p]).StylesChanged, Is.False);
    }

    [Test]
    public void FailureInSecondLinkedFileCannotPartiallyApplyFirstFile()
    {
        var files = Files(); files[Path] = Markup + "<link rel=stylesheet href=second.css>";
        files["res://ui/second.css"] = "button{height:20px}";
        var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        var first = loaded.Document.StyleSheets[0]; var second = loaded.Document.StyleSheets[1];
        files[CssPath] = "button{width:80px}"; files.Remove("res://ui/second.css");
        Assert.Throws<KeyNotFoundException>(() => loaded.PrepareUpdate(p => files[p]));
        Assert.That(loaded.Document.StyleSheets[0], Is.SameAs(first));
        Assert.That(loaded.Document.StyleSheets[1], Is.SameAs(second));
    }

    [Test]
    public void CurrentHrefAndDocumentBaseDetermineDependenciesAndStylesheetBase()
    {
        var files = Files(); var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        loaded.Document.BaseUrl = NecronomiDom.Url.WebUrl.Parse("res:///other/screen.html");
        loaded.Document.QuerySelector("link")!.SetAttribute("href", "../assets/theme.css");
        files["res://assets/theme.css"] = "#action{width:90px}";
        var read = new List<string>();
        var update = loaded.PrepareUpdate(p => { read.Add(p); return files[p]; });
        Assert.That(read, Is.EquivalentTo(new[] { Path, "res://assets/theme.css" }));
        update.ApplyStyles();
        Assert.That(loaded.Document.StyleSheets[0].SourcePath, Is.EqualTo("res://assets/theme.css"));
        Assert.That(loaded.Document.StyleSheets[0].BaseUrl!.Href, Is.EqualTo("res:///assets/theme.css"));
        Assert.That(loaded.DependencyCount, Is.EqualTo(2));
    }

    [Test]
    public void DuplicateLinksReadFileOnceAndRetainDistinctOwnersAndMedia()
    {
        var files = Files(); files[Path] = Markup + "<link rel=stylesheet href=css/theme.css media=screen>";
        int reads = 0;
        var loaded = DocumentFileReload.Prepare(Path, p => { if (p == CssPath) reads++; return files[p]; });
        Assert.That(reads, Is.EqualTo(1)); Assert.That(loaded.Document.StyleSheets.Count, Is.EqualTo(2));
        Assert.That(loaded.Document.StyleSheets[0].OwnerNode, Is.Not.SameAs(loaded.Document.StyleSheets[1].OwnerNode));
        Assert.That(loaded.Document.StyleSheets[1].Media.MediaText, Is.EqualTo("screen"));
    }

    [Test]
    public void MediaSerializationDoesNotCauseRepeatedFalseChanges()
    {
        var files = Files(); files[Path] = Markup.Replace("href=css/theme.css", "href=css/theme.css media='SCREEN and ( min-width : 1px )'", StringComparison.Ordinal);
        var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        Assert.That(loaded.PrepareUpdate(p => files[p]).StylesChanged, Is.False);
        loaded.Document.QuerySelector("link")!.SetAttribute("media", "print");
        var update = loaded.PrepareUpdate(p => files[p]);
        Assert.That(update.StylesChanged, Is.True);
        update.ApplyStyles();
        Assert.That(loaded.PrepareUpdate(p => files[p]).StylesChanged, Is.False);
    }

    [Test]
    public void RemovingRuntimeLinkRetiresDependencyWithoutRemovingUnrelatedExternalSheets()
    {
        var files = Files(); var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        var unrelated = CSSStyleSheet.Parse("button{height:21px}"); loaded.Document.StyleSheets.Add(unrelated);
        var link = loaded.Document.QuerySelector("link")!;
        link.Remove();
        var update = loaded.PrepareUpdate(p => files[p]); update.ApplyStyles();
        Assert.That(loaded.DependencyCount, Is.EqualTo(1));
        Assert.That(loaded.Document.StyleSheets.Count, Is.EqualTo(1));
        Assert.That(loaded.Document.StyleSheets[0], Is.SameAs(unrelated));
        loaded.Document.Head!.AppendChild(link);
        loaded.PrepareUpdate(p => files[p]).ApplyStyles();
        Assert.That(loaded.Document.StyleSheets.Count, Is.EqualTo(2), "Reattaching the old owner must not revive a retired file sheet.");
    }

    [Test]
    public void LinkedReplacementPreservesCompetingProgrammaticSheetOrder()
    {
        var files = Files(); files[CssPath] = "#action{color:red}";
        var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        var current = loaded.Document.StyleSheets;
        var linkedOwner = current[0].OwnerNode;
        var preceding = CSSStyleSheet.Parse("#action{color:yellow}"); preceding.OwnerNode = linkedOwner;
        var following = CSSStyleSheet.Parse("#action{color:blue}"); following.OwnerNode = linkedOwner;
        current.Insert(0, preceding); current.Add(following);
        var button = loaded.Document.GetElementById("action")!;
        var styles = new StyleEngine(loaded.Document);
        string expectedColor = styles.GetComputedStyle(button).GetPropertyValue("color");
        Assert.That(expectedColor, Is.EqualTo("rgb(0, 0, 255)"));
        files[CssPath] = "#action{color:green}";
        loaded.PrepareUpdate(p => files[p]).ApplyStyles();
        Assert.That(current[0], Is.SameAs(preceding));
        Assert.That(current[2], Is.SameAs(following));
        Assert.That(styles.GetComputedStyle(button).GetPropertyValue("color"), Is.EqualTo(expectedColor),
            "Reloaded file CSS must not move after the competing game-owned stylesheet.");
        var adopted = new CSSStyleSheet(); adopted.ReplaceSync("#action{color:purple}");
        loaded.Document.AdoptedStyleSheets.Add(adopted);
        string adoptedColor = styles.GetComputedStyle(button).GetPropertyValue("color");
        files[CssPath] = "#action{color:orange}";
        loaded.PrepareUpdate(p => files[p]).ApplyStyles();
        Assert.That(styles.GetComputedStyle(button).GetPropertyValue("color"), Is.EqualTo(adoptedColor));
        Assert.That(loaded.Document.AdoptedStyleSheets[0], Is.SameAs(adopted));
    }

    [Test]
    public void OrdinaryLoadingDoesNotAcquireDeveloperReloadLimits()
    {
        var files = Files();
        files[CssPath] = new string(' ', DocumentFileReload.MaxFileCharacters + 1);
        var loaded = DocumentFileReload.Load(Path, p => files[p]);
        Assert.That(loaded.Document.StyleSheets.Count, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => DocumentFileReload.Attach(loaded.Document, Path, p => files[p]));
        Assert.Throws<InvalidOperationException>(() => DocumentFileReload.Prepare(Path, p => files[p]));
        files[CssPath] = Css;
        files[Path] = Markup;
        var manyLinks = new System.Text.StringBuilder(Markup);
        for (int i = 0; i < DocumentFileReload.MaxFiles; i++) manyLinks.Append("<link rel=stylesheet href=css/theme.css>");
        files[Path] = manyLinks.ToString();
        loaded = DocumentFileReload.Load(Path, p => files[p]);
        Assert.That(loaded.Document.StyleSheets.Count, Is.GreaterThan(DocumentFileReload.MaxFiles));
        Assert.Throws<InvalidOperationException>(() => DocumentFileReload.Attach(loaded.Document, Path, p => files[p]));
    }

    [Test]
    public void EnablingAfterOrdinaryLoadEstablishesBaselineWithoutReplacingDomOrCss()
    {
        var files = Files();
        var loaded = DocumentFileReload.Load(Path, p => files[p]);
        var field = (HTMLInputElement)loaded.Document.GetElementById("field")!;
        field.Value = "game value";
        var originalSheet = loaded.Document.StyleSheets[0];
        files[CssPath] = "#action{width:80px}";
        var tracking = DocumentFileReload.Attach(loaded.Document, Path, p => files[p]);
        Assert.That(tracking.Document, Is.SameAs(loaded.Document));
        Assert.That(tracking.Document.StyleSheets[0], Is.SameAs(originalSheet));
        Assert.That(field.Value, Is.EqualTo("game value"));
        Assert.That(tracking.PrepareUpdate(p => files[p]).StylesChanged, Is.False,
            "Edits predating enable establish the baseline; force reload explicitly applies them.");
        Assert.That(tracking.PrepareUpdate(p => files[p], true).Replacement, Is.Not.Null);
        files[CssPath] = "#action{width:90px}";
        var update = tracking.PrepareUpdate(p => files[p]); update.ApplyStyles();
        Assert.That(new StyleEngine(loaded.Document).GetComputedStyle(loaded.Document.GetElementById("action")!).GetPropertyValue("width"), Is.EqualTo("90px"));
        Assert.That(tracking.Document.GetElementById("field"), Is.SameAs(field));
        Assert.That(field.Value, Is.EqualTo("game value"));
    }

    [Test]
    public void SourceSizeAndDependencyCountAreBoundedBeforeReplacingLiveState()
    {
        var files = Files(); var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        files[CssPath] = new string(' ', DocumentFileReload.MaxFileCharacters + 1);
        Assert.Throws<InvalidOperationException>(() => loaded.PrepareUpdate(p => files[p]));
        files[CssPath] = Css;
        var head = loaded.Document.Head!;
        for (int i = 0; i < DocumentFileReload.MaxFiles; i++)
        {
            var link = loaded.Document.CreateElement("link"); link.SetAttribute("rel", "stylesheet"); link.SetAttribute("href", "css/theme.css"); head.AppendChild(link);
        }
        Assert.Throws<InvalidOperationException>(() => loaded.PrepareUpdate(p => files[p]));
    }

    [Test]
    public void CssParserRecoveryReportsDiagnosticsWithoutChangingTheParsingContract()
    {
        var files = Files(); var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        files[CssPath] = "@not-a-runtime-rule; #action{width:81px}";
        var update = loaded.PrepareUpdate(p => files[p]);
        Assert.That(update.Diagnostics, Is.Not.Empty);
        Assert.That(update.StylesChanged, Is.True);
    }

    [Test]
    public void InvalidMarkupReplacementLeavesOriginalDocumentAvailable()
    {
        var files = Files(); var loaded = DocumentFileReload.Prepare(Path, p => files[p]);
        files[Path] = "<link rel=stylesheet href=https://example.com/theme.css><input id=new>";
        Assert.Throws<NotSupportedException>(() => loaded.PrepareUpdate(p => files[p]));
        Assert.That(loaded.Document.GetElementById("field"), Is.Not.Null);
        Assert.That(loaded.Document.GetElementById("new"), Is.Null);
    }
}
