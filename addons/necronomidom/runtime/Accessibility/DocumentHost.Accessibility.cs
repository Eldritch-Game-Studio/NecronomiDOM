using Godot;
using NecronomiDom.Accessibility;
using NecronomiDom.Dom;
using Node = NecronomiDom.Dom.Node;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    private AccessibleTree? _accessibleTree;
    private NativeAccessibleDocument? _nativeAccessibility;
    private Element? _accessibleFocus;
    private int _accessibleLayout = -1;
    private bool _accessibilityActive;
    private Callable? _documentAccessibilityFocus;
    public AccessibleTree? AccessibilityTree => _accessibleTree;
    public int AccessibleSubElementCount => _nativeAccessibility?.ElementCount ?? 0;
    public int AccessibilityPublicationCount => _nativeAccessibility?.PublishedUpdates ?? 0;
    public Rid GetDocumentAccessibilityElement(Node source) => _nativeAccessibility?.Get(source) ?? default;
    private void MountDocumentAccessibility()
    {
        WithdrawDocumentAccessibility();
        _accessibleTree = new AccessibleTree(Document, IsAccessibilityHidden, IsAccessibilitySubtreeHidden,
            element => Styles.GetComputedStyle(element).GetPropertyValue("display"));
        // The engine-owned Host RID may outlive a document; its action resolves the current document, never the retired tree.
        if (!_documentAccessibilityFocus.HasValue) _documentAccessibilityFocus = Callable.From<Variant>(FocusDocumentAccessibility);
        _nativeAccessibility = new NativeAccessibleDocument(_accessibleTree, _documentAccessibilityFocus.Value);
        _accessibleLayout = -1; _accessibleFocus = null; _accessibilityActive = false;
        QueueAccessibilityUpdate();
    }
    private void FocusDocumentAccessibility(Variant _)
    {
        if (_accessibleTree != null) Document.FocusedElement?.Blur();
    }
    private bool IsAccessibilityHidden(Element element)
    {
        var style = Styles.GetComputedStyle(element);
        return style.GetPropertyValue("display") == "none" || style.GetPropertyValue("visibility") is "hidden" or "collapse" || style.GetPropertyValue("content-visibility") == "hidden";
    }
    private bool IsAccessibilitySubtreeHidden(Element element)
    {
        var style = Styles.GetComputedStyle(element);
        return style.GetPropertyValue("display") == "none" || style.GetPropertyValue("content-visibility") == "hidden";
    }
    private void SynchronizeDocumentAccessibility()
    {
        if (_accessibleTree == null || _nativeAccessibility == null || _dirty) return;
        // Forced support=1 is the documented public inspector/test mode, not a pretend assistive client.
        bool active = AccessibilityServer.IsSupported() && (DisplayServer.AccessibilityScreenReaderActive() == 1 || ProjectSettings.GetSetting("accessibility/general/accessibility_support", 0).AsInt32() == 1);
        if (!active) { _accessibilityActive = false; return; }
        bool geometry = _accessibleLayout != GeometryRevision;
        bool focus = !ReferenceEquals(_accessibleFocus, Document.FocusedElement);
        if (!_accessibilityActive || geometry || focus || _accessibleTree.IsDirty)
        {
            if (geometry) _accessibleTree.InvalidateRendering();
            bool pending = _nativeAccessibility.Prepare(DisplayList, geometry);
            _accessibleLayout = GeometryRevision; _accessibleFocus = Document.FocusedElement; _accessibilityActive = true;
            if (pending || focus) QueueAccessibilityUpdate();
        }
    }
    private void WithdrawDocumentAccessibility()
    {
        _nativeAccessibility?.Dispose(); _nativeAccessibility = null;
        // Restore the actual prior sink before Host disposes DocumentInvalidation.
        _accessibleTree?.Dispose(); _accessibleTree = null;
        _accessibleFocus = null; _accessibleLayout = -1; _accessibilityActive = false;
    }
    public override Rid _GetFocusedAccessibilityElement() => _nativeAccessibility?.Focused(Document?.FocusedElement, GetAccessibilityElement()) ?? GetAccessibilityElement();
    public override void _Notification(int what)
    {
        // Before the first Ready the paint node has not acquired a parent to free it.
        if (what == NotificationPredelete && !_readyInitialized) _paint.Free();
        if (what == NotificationAccessibilityInvalidate)
        {
            _nativeAccessibility?.Invalidate(); _accessibilityActive = false;
        }
        else if (what == NotificationAccessibilityUpdate && _accessibilityActive && !_dirty)
        {
            _nativeAccessibility?.Publish(GetAccessibilityElement());
        }
    }
}
