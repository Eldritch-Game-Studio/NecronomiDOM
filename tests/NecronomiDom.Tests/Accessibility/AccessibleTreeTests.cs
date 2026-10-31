using System;
using System.Linq;
using NecronomiDom.Accessibility;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Accessibility;

[TestFixture]
public sealed class AccessibleTreeTests
{
    private static Document Parse(string body) => HtmlImporter.Import("<!doctype html><html><head><title>AX regression</title></head><body>" + body + "</body></html>", "ax", WebUrl.Parse("res:///ax.html")!).Document;
    private static AccessibleNode Ax(AccessibleTree tree, Document doc, string id) => tree.Get(doc.GetElementById(id)!)!;
    [TestCase("<button id=x>Launch</button>", "button", "Launch")]
    [TestCase("<h3 id=x>Heading</h3>", "heading", "Heading")]
    [TestCase("<img id=x alt='Portrait'>", "img", "Portrait")]
    [TestCase("<input id=x type=submit>", "button", "Submit")]
    [TestCase("<button id=x role='abstract bogus switch'>Power</button>", "switch", "Power")]
    [TestCase("<input id=x type=range role=button aria-label=Volume>", "slider", "Volume")]
    [TestCase("<div id=x role=button>Custom action</div>", "button", "Custom action")]
    [TestCase("<p id=x aria-label=Forbidden>Words</p>", "paragraph", "")]
    public void NativeRolesPermittedExplicitTokensAndNamingProhibition(string html, string role, string name)
    {
        var doc = Parse(html); using var tree = new AccessibleTree(doc); tree.Synchronize();
        Assert.That(Ax(tree, doc, "x").Role, Is.EqualTo(role)); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo(name));
    }
    [Test]
    public void NamePrecedenceSelfReferenceAndDescriptionAreSeparate()
    {
        var doc = Parse("<span id=first hidden> Hidden label </span><span id=last>Suffix</span><button id=x aria-labelledby='missing first x last' aria-label=Self title=Tip aria-describedby=desc>Content</button><p id=desc hidden>Description</p>");
        using var tree = new AccessibleTree(doc); tree.Synchronize(); var ax = Ax(tree, doc, "x");
        Assert.That(ax.Name, Is.EqualTo("Hidden label Self Suffix")); Assert.That(ax.Description, Is.EqualTo("Description"));
        Assert.That(tree.Get(doc.GetElementById("first")!), Is.Null);
        doc.GetElementById("x")!.RemoveAttribute("aria-labelledby"); tree.Synchronize(); Assert.That(ax.Name, Is.EqualTo("Self"));
        doc.GetElementById("x")!.RemoveAttribute("aria-label"); tree.Synchronize(); Assert.That(ax.Name, Is.EqualTo("Content"));
        doc.GetElementById("x")!.RemoveAttribute("aria-describedby"); tree.Synchronize(); Assert.That(ax.Description, Is.EqualTo("Tip"));
    }
    [Test]
    public void ReferenceRecursionStopsLabelledByChainsAndContentCycles()
    {
        var doc = Parse("<span id=a aria-labelledby=b>A</span><span id=b aria-labelledby=a>B</span><button id=x aria-labelledby=a>Ignored</button>");
        using var tree = new AccessibleTree(doc); tree.Synchronize(); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("A"));
        doc.GetElementById("a")!.SetAttribute("aria-labelledby", "a"); tree.Synchronize(); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("A"));
    }
    [Test]
    public void VisibleReferenceExcludesHiddenDescendantHiddenReferenceIncludesIt()
    {
        var doc = Parse("<div id=label>Visible<span hidden>Secret</span></div><button id=x aria-labelledby=label>X</button>");
        using var tree = new AccessibleTree(doc); tree.Synchronize(); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("Visible"));
        doc.GetElementById("label")!.SetAttribute("hidden", ""); tree.Synchronize(); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("VisibleSecret"));
    }
    [Test]
    public void LabelsLegendsCaptionsAndEmbeddedCurrentValue()
    {
        var doc = Parse("<label for=x>Speed <input id=value value=12></label><input id=x><fieldset id=f><legend>Captain</legend><input></fieldset><table id=t><caption>Manifest</caption></table>");
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("Speed 12")); Assert.That(Ax(tree, doc, "f").Name, Is.EqualTo("Captain")); Assert.That(Ax(tree, doc, "t").Name, Is.EqualTo("Manifest"));
        ((HTMLInputElement)doc.GetElementById("value")!).Value = "20"; tree.Synchronize(); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("Speed 20"));
    }
    [Test]
    public void ReferenceMutationInvalidatesDependentNotUnrelatedAndCleanFramesAllocateNothing()
    {
        var doc = Parse("<span id=label>Before</span><button id=x aria-labelledby=label></button><button id=y>Unchanged</button>");
        using var tree = new AccessibleTree(doc); tree.Synchronize(); var x = Ax(tree, doc, "x"); var y = Ax(tree, doc, "y"); int revision = y.Revision;
        ((Text)doc.GetElementById("label")!.FirstChild!).Data = "After"; tree.Synchronize();
        Assert.That(x.Name, Is.EqualTo("After")); Assert.That(y.Revision, Is.EqualTo(revision)); Assert.That(tree.Changed, Does.Contain(x)); Assert.That(tree.Changed, Does.Not.Contain(y));
        tree.Synchronize();
        Assert.That(AllocationWindow.Measure(100, _ => AssertClean(tree)), Is.Zero);
    }
    private static void AssertClean(AccessibleTree tree) { if (tree.Synchronize()) throw new Exception("Clean projection changed"); }
    [Test]
    public void HiddenInertRemovalReinsertionAndAuthorIdKeepActualIdentity()
    {
        var doc = Parse("<div id=parent><button id=x>X</button></div>"); using var tree = new AccessibleTree(doc); tree.Synchronize();
        var element = doc.GetElementById("x")!; var original = tree.Get(element)!;
        element.SetAttribute("id", "different"); tree.Synchronize(); Assert.That(tree.Get(element), Is.SameAs(original));
        var parent = doc.GetElementById("parent")!; parent.SetAttribute("inert", ""); tree.Synchronize(); Assert.That(tree.Get(element), Is.Null); Assert.That(tree.Removed, Does.Contain(original));
        parent.RemoveAttribute("inert"); tree.Synchronize(); Assert.That(tree.Get(element), Is.SameAs(original));
        element.Remove(); tree.Synchronize(); Assert.That(tree.Get(element), Is.Null);
        parent.AppendChild(element); tree.Synchronize(); Assert.That(tree.Get(element), Is.SameAs(original));
        element.SetAttribute("aria-hidden", "true"); tree.Synchronize(); Assert.That(tree.Get(element), Is.Null);
    }
    [Test]
    public void FocusAndDetachedFocusRemovalDoNotRequireBlurEvents()
    {
        var doc = Parse("<button id=x>X</button><button id=y>Y</button>"); using var tree = new AccessibleTree(doc); tree.Synchronize();
        var x = (HTMLElement)doc.GetElementById("x")!; x.Focus(); tree.Synchronize(); Assert.That(tree.Get(x)!.States.Focused, Is.True);
        x.Remove(); tree.Synchronize(); Assert.That(doc.FocusedElement, Is.Null); Assert.That(tree.Get(x), Is.Null);
        ((HTMLElement)doc.GetElementById("y")!).Focus(); tree.Synchronize(); Assert.That(Ax(tree, doc, "y").States.Focused, Is.True);
    }
    [Test]
    public void SemanticFocusabilityDoesNotBypassActualRenderedFocusEligibility()
    {
        var doc = Parse("<button id=x role=presentation>X</button><div id=y tabindex=-1>Y</div>");
        doc.IsRenderedForFocus = _ => false;
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        var x = (HTMLElement)doc.GetElementById("x")!;
        Assert.That(Ax(tree, doc, "x").Role, Is.EqualTo("button"));
        Assert.That(Ax(tree, doc, "x").States.Focusable, Is.True);
        Assert.That(Ax(tree, doc, "y").States.Focusable, Is.True);
        Assert.That(doc.IsFocusable(x), Is.False); Assert.That(doc.Focus(x), Is.False);
        Assert.That(doc.FocusedElement, Is.Null);
        x.SetAttribute("disabled", ""); tree.Synchronize();
        Assert.That(Ax(tree, doc, "x").States.Focusable, Is.False);
    }
    [Test]
    public void AuthoredTextboxValueAndTypedInvalidFollowRealContents()
    {
        var doc = Parse("<div id=x role=textbox contenteditable=true tabindex=0>Draft</div>");
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        var x = doc.GetElementById("x")!;
        Assert.That(tree.Get(x)!.States.Value, Is.EqualTo("Draft"));
        foreach (string invalid in new[] { "true", "grammar", "spelling", "false" })
        {
            x.SetAttribute("aria-invalid", invalid); tree.Synchronize();
            Assert.That(tree.Get(x)!.States.Invalid, Is.EqualTo(invalid));
        }
        x.TextContent = "Revised"; tree.Synchronize();
        Assert.That(tree.Get(x)!.States.Value, Is.EqualTo("Revised"));
        x.RemoveAttribute("aria-invalid"); tree.Synchronize();
        Assert.That(tree.Get(x)!.States.Invalid, Is.EqualTo("false"));
    }
    [Test]
    public void NativeCheckedValuesInheritedDisabledAndLiveStatesUpdate()
    {
        var doc = Parse("<div id=group aria-live=polite aria-disabled=true><input id=x type=checkbox checked required><h2 id=h>Title</h2><input id=range type=range min=5 max=25 value=15></div>");
        using var tree = new AccessibleTree(doc); tree.Synchronize(); var x = (HTMLInputElement)doc.GetElementById("x")!;
        Assert.That(tree.Get(x)!.States.Checked, Is.EqualTo(AccessibleChecked.True)); Assert.That(tree.Get(x)!.States.Disabled, Is.True); Assert.That(tree.Get(x)!.States.Required, Is.True);
        x.Indeterminate = true; tree.Synchronize(); Assert.That(tree.Get(x)!.States.Checked, Is.EqualTo(AccessibleChecked.Mixed));
        Assert.That(Ax(tree, doc, "h").Level, Is.EqualTo(2)); Assert.That(Ax(tree, doc, "h").Live, Is.EqualTo(AccessibleLive.Polite));
        doc.GetElementById("group")!.SetAttribute("aria-live", "assertive"); tree.Synchronize(); Assert.That(Ax(tree, doc, "h").Live, Is.EqualTo(AccessibleLive.Assertive));
        Assert.That(Ax(tree, doc, "range").States.ValueNow, Is.EqualTo(15));
    }
    [Test]
    public void ComposedSlotsExcludeUnassignedLightDomAndScopeReferences()
    {
        var doc = Parse("<div id=host><button slot=action id=x>Launch</button><button id=unused>Unused</button></div>");
        var shadow = doc.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var slot = doc.CreateElement("slot"); slot.SetAttribute("name", "action"); shadow.AppendChild(slot);
        using var tree = new AccessibleTree(doc); tree.Synchronize(); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("Launch")); Assert.That(tree.Get(doc.GetElementById("unused")!), Is.Null);
        doc.GetElementById("x")!.SetAttribute("slot", "other"); tree.Synchronize(); Assert.That(tree.Get(doc.GetElementById("x")!), Is.Null);
    }
    [Test]
    public void RelationsOwnsCycleProtectionAndRemovalRecompute()
    {
        var doc = Parse("<div id=a role=group aria-owns=b aria-controls=b></div><button id=b aria-owns=a>B</button>");
        using var tree = new AccessibleTree(doc); tree.Synchronize(); var a = Ax(tree, doc, "a"); var b = Ax(tree, doc, "b");
        Assert.That(b.Parent, Is.SameAs(a)); Assert.That(a.Relations["controls"].Single(), Is.SameAs(b));
        doc.GetElementById("b")!.Remove(); tree.Synchronize(); Assert.That(a.Relations["controls"], Is.Empty);
    }
    [Test]
    public void NativeLabelRelationsAndIgnoredStructureDoNotLoseAccessibleName()
    {
        var doc = Parse("<label id=label for=x>Provision</label><input id=x type=checkbox required>");
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        Assert.That(tree.Get(doc.DocumentElement!)!.Ignored, Is.True);
        Assert.That(tree.Get(doc.Body!)!.Role, Is.EqualTo("none"));
        Assert.That(Ax(tree, doc, "label").Ignored, Is.True);
        Assert.That(Ax(tree, doc, "x").Relations["labelledby"].Single(), Is.SameAs(Ax(tree, doc, "label")));
        Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("Provision"));
        Assert.That(Ax(tree, doc, "x").States.Required, Is.True, "HTML-AAM required is retained even when Chrome omits it.");
    }
    [Test]
    public void VisibilityOverrideIsNotDisplayNoneAndNameUsesActualComputedDisplay()
    {
        var doc = Parse("<div id=parent><button id=visible>Visible</button></div><button id=x>Hello<span id=part>world</span></button>");
        bool block = false;
        using var tree = new AccessibleTree(doc, e => e.Id == "parent", _ => false, e => e.Id == "part" && block ? "block" : "inline");
        tree.Synchronize();
        Assert.That(Ax(tree, doc, "parent").Ignored, Is.True);
        Assert.That(Ax(tree, doc, "visible").Name, Is.EqualTo("Visible"));
        Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("Helloworld"));
        block = true; tree.InvalidateRendering(); tree.Synchronize(); Assert.That(Ax(tree, doc, "x").Name, Is.EqualTo("Hello world"));
    }
    [Test]
    public void NativeFieldsetAndUnsupportedAriaStateInvalidation()
    {
        var doc = Parse("<fieldset id=f disabled><input id=x></fieldset><div id=generic aria-selected=true aria-required=true aria-readonly=true></div>");
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        Assert.That(Ax(tree, doc, "x").States.Disabled, Is.True);
        doc.GetElementById("f")!.RemoveAttribute("disabled"); tree.Synchronize(); Assert.That(Ax(tree, doc, "x").States.Disabled, Is.False);
        Assert.That(Ax(tree, doc, "generic").States.Selected, Is.Null);
        Assert.That(Ax(tree, doc, "generic").States.Required, Is.False);
        Assert.That(Ax(tree, doc, "generic").States.ReadOnly, Is.False);
    }
    [Test]
    public void EmptyTextMutationUpdatesComposedMembershipWithoutReplacingParentIdentity()
    {
        var doc = Parse("<button id=x>Before</button>"); using var tree = new AccessibleTree(doc); tree.Synchronize();
        var parent = Ax(tree, doc, "x"); var text = (Text)doc.GetElementById("x")!.FirstChild!;
        text.Data = " "; tree.Synchronize(); Assert.That(parent.Children, Is.Empty); Assert.That(parent.Name, Is.Empty);
        text.Data = "After"; tree.Synchronize(); Assert.That(parent.Children.Count, Is.EqualTo(1)); Assert.That(parent.Name, Is.EqualTo("After"));
    }
    [Test]
    public void LiveAtomicRelevantInheritanceAndDependencyInvalidation()
    {
        var doc = Parse("<div id=status role=status><span id=child>Ready</span></div>");
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        var child = Ax(tree, doc, "child"); var text = tree.Get(doc.GetElementById("child")!.FirstChild!)!;
        Assert.That(child.Live, Is.EqualTo(AccessibleLive.Polite)); Assert.That(child.LiveAtomic, Is.True);
        Assert.That(text.Live, Is.EqualTo(AccessibleLive.Polite)); Assert.That(text.LiveAtomic, Is.True);
        doc.GetElementById("status")!.SetAttribute("aria-atomic", "false");
        doc.GetElementById("status")!.SetAttribute("aria-relevant", "removals");
        tree.Synchronize();
        Assert.That(child.LiveAtomic, Is.False); Assert.That(text.LiveAtomic, Is.False);
        Assert.That(child.LiveRelevant, Is.EqualTo("removals")); Assert.That(text.LiveRelevant, Is.EqualTo("removals"));
    }
    [Test]
    public void HiddenRelationTargetsRetainIdentityWithoutBecomingExposed()
    {
        var doc = Parse("<button id=x aria-labelledby=label aria-describedby=detail>X</button><span id=label hidden>Hidden label</span><span id=detail inert>Help</span>");
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        var button = Ax(tree, doc, "x"); var target = button.Relations["labelledby"][0];
        Assert.That(target.Source, Is.SameAs(doc.GetElementById("label"))); Assert.That(target.Exposed, Is.False);
        Assert.That(tree.Get(target.Source), Is.Null); Assert.That(button.Description, Is.EqualTo("Help"));
        Assert.That(button.Relations["describedby"][0].Source, Is.SameAs(doc.GetElementById("detail")));
        doc.GetElementById("label")!.RemoveAttribute("hidden"); tree.Synchronize();
        Assert.That(tree.Get(target.Source), Is.SameAs(target)); Assert.That(target.Exposed, Is.True);
        doc.GetElementById("detail")!.Remove(); tree.Synchronize(); Assert.That(button.Relations["describedby"], Is.Empty);
    }
    [Test]
    public void OwnedContentsNameAndMembershipDoNotDirtyUnchangedOwnership()
    {
        var doc = Parse("<button id=x aria-owns=y></button><span id=y>Owned label</span>");
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        var button = Ax(tree, doc, "x"); var owned = Ax(tree, doc, "y");
        Assert.That(button.Name, Is.EqualTo("Owned label")); Assert.That(owned.Parent, Is.SameAs(button));
        int revision = button.Revision; tree.InvalidateRendering(); tree.Synchronize();
        Assert.That(button.Revision, Is.EqualTo(revision), "A recomposition cannot manufacture dirty ownership.");
        doc.GetElementById("y")!.TextContent = "Changed"; tree.Synchronize(); Assert.That(button.Name, Is.EqualTo("Changed"));
        doc.GetElementById("x")!.RemoveAttribute("aria-owns"); tree.Synchronize();
        Assert.That(button.Name, Is.Empty); Assert.That(button.Children, Is.Empty);
    }
    [Test]
    public void DisposalRestoresEarlierSinkAndDetachesOldDocument()
    {
        var doc = Parse("<button id=x>X</button>"); int changes = 0; using var invalidation = new DocumentInvalidation(doc, () => changes++);
        var tree = new AccessibleTree(doc); tree.Synchronize(); var retired = Ax(tree, doc, "x"); tree.Dispose(); tree.Dispose();
        Assert.That(retired.Exposed, Is.False, "Retired action receivers cannot remain exposed after withdrawal."); Assert.That(retired.Parent, Is.Null);
        doc.GetElementById("x")!.SetAttribute("aria-label", "After disposal"); Assert.That(changes, Is.EqualTo(1)); Assert.That(tree.Nodes, Is.Empty);
        Assert.Throws<ObjectDisposedException>(() => tree.Synchronize());
    }
}
