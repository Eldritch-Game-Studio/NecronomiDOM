using NecronomiDom.Accessibility;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Layout;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Accessibility;

[TestFixture]
public sealed class ManualSlotAccessibilityTests
{
    [Test]
    public void ReassignmentDirtiesActualComposedProjectionAndRetainedInvalidationWithoutFakeMutation()
    {
        var doc = HtmlImporter.Import("<!doctype html><html><body><div id=host><span id=a>First</span><span id=b>Second</span></div></body></html>",
            "manual-slot", WebUrl.Parse("res:///manual-slot.html")!).Document;
        _ = doc.DefaultView;
        var shadow = doc.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open, SlotAssignment = SlotAssignmentMode.Manual });
        var button = doc.CreateElement("button"); var slot = (HTMLSlotElement)doc.CreateElement("slot");
        button.AppendChild(slot); shadow.AppendChild(button);
        var first = doc.GetElementById("a")!; var second = doc.GetElementById("b")!;
        slot.Assign(first);
        int retainedDirty = 0;
        using var invalidation = new DocumentInvalidation(doc, () => retainedDirty++);
        using var tree = new AccessibleTree(doc); tree.Synchronize();
        var original = tree.Get(button)!; Assert.That(original.Name, Is.EqualTo("First"));
        var observer = new MutationObserver((_, _) => { });
        observer.Observe(shadow, new MutationObserverInit { ChildList = true, Subtree = true });
        slot.Assign(second);
        Assert.That(retainedDirty, Is.EqualTo(1));
        Assert.That(tree.IsDirty, Is.True); tree.Synchronize();
        Assert.That(tree.Get(button), Is.SameAs(original)); Assert.That(original.Name, Is.EqualTo("Second"));
        Assert.That(tree.Get(first), Is.Null); Assert.That(tree.Get(second), Is.Not.Null);
        Assert.That(observer.TakeRecords(), Is.Empty, "Distribution changes are not physical DOM child-list mutations.");
        slot.Assign(second, second); slot.Assign(second);
        Assert.That(retainedDirty, Is.EqualTo(1), "Equivalent de-duplicated assignments retain no-op dirtiness semantics.");
        Assert.That(tree.IsDirty, Is.False);
        observer.Disconnect();
    }
}
