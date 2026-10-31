using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NecronomiDom.Dom;

namespace NecronomiDom.Accessibility;

/// <summary>
/// One composed DOM accessibility projection. Mutations are chained synchronously through the existing DOM sink;
/// clean Synchronize calls do not enumerate the tree or allocate. Hidden referenced nodes participate in names,
/// never in publication. https://www.w3.org/TR/accname-1.2/ and https://www.w3.org/TR/html-aam-1.0/.
/// </summary>
public sealed partial class AccessibleTree : IDomChangeSink, IDisposable
{
    private readonly Document _document;
    private readonly IDomChangeSink? _previous;
    private readonly Func<Element, bool>? _renderHidden;
    private readonly Func<Element, bool>? _renderSubtreeHidden;
    private readonly Func<Element, string>? _display;
    private readonly ConditionalWeakTable<Node, AccessibleNode> _identity = new();
    private readonly Dictionary<Node, AccessibleNode> _nodes = new();
    private readonly List<AccessibleNode> _ordered = new();
    private readonly List<AccessibleNode> _changed = new();
    private readonly List<AccessibleNode> _removed = new();
    private readonly HashSet<Node> _dirty = new();
    private readonly HashSet<Node> _seen = new();
    private readonly HashSet<AccessibleNode> _owned = new();
    private readonly List<AccessibleNode> _ownershipOrder = new();
    private readonly Dictionary<Node, HashSet<Node>> _dependents = new();
    private readonly Dictionary<Node, HashSet<Node>> _dependencies = new();
    private readonly Dictionary<string, List<Element>> _idReferences = new(StringComparer.Ordinal);
    private long _nextIdentity;
    private bool _structureDirty = true;
    private bool _disposed;
    private Element? _focus;
    private Node? _computing;
    public AccessibleTree(Document document, Func<Element, bool>? renderHidden = null,
        Func<Element, bool>? renderSubtreeHidden = null, Func<Element, string>? display = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        // Event propagation reaches DefaultView. Create its existing style sink BEFORE stacking AX,
        // otherwise the first focus event would install a new consumer above our detachable sink.
        _ = document.DefaultView;
        _renderHidden = renderHidden; _renderSubtreeHidden = renderSubtreeHidden ?? renderHidden; _display = display;
        _previous = document.ChangeSink; document.ChangeSink = this;
        Root = Identity(document); Root.Role = "RootWebArea"; Root.Exposed = true; _nodes.Add(document, Root);
    }
    public AccessibleNode Root { get; }
    public IReadOnlyList<AccessibleNode> Nodes => _ordered;
    public IReadOnlyList<AccessibleNode> Changed => _changed;
    public IReadOnlyList<AccessibleNode> Removed => _removed;
    public bool IsDirty => _structureDirty || _dirty.Count != 0 || !ReferenceEquals(_focus, _document.FocusedElement);
    public AccessibleNode? Get(Node source) => _nodes.GetValueOrDefault(source);
    private AccessibleNode Identity(Node node)
    {
        if (_identity.TryGetValue(node, out var existing)) return existing;
        var created = new AccessibleNode(++_nextIdentity, node); _identity.Add(node, created); return created;
    }
    public bool Synchronize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _changed.Clear(); _removed.Clear();
        if (!IsDirty) return false;
        if (!ReferenceEquals(_focus, _document.FocusedElement))
        {
            if (_focus != null) Mark(_focus);
            _focus = _document.FocusedElement;
            if (_focus != null) Mark(_focus);
        }
        if (_structureDirty) Recompose();
        foreach (var node in _dirty)
            if (_nodes.TryGetValue(node, out var ax)) Compute(ax);
        _dirty.Clear(); _structureDirty = false;
        return _changed.Count != 0 || _removed.Count != 0;
    }
    /// <summary>Called after stylesheet/media/layout changes; does not publish or run in clean frames.</summary>
    public void InvalidateRendering() { ObjectDisposedException.ThrowIf(_disposed, this); _structureDirty = true; }
    private void Recompose()
    {
        _seen.Clear(); _ordered.Clear(); _idReferences.Clear();
        foreach (Node n in _document.ShadowIncludingInclusiveDescendants())
            if (n is Element e && e.Id.Length != 0)
            {
                if (!_idReferences.TryGetValue(e.Id, out var list)) _idReferences[e.Id] = list = new();
                list.Add(e);
            }
        Visit(_document, null, false, false);
        // Removing sources must release their dependency edges; the weak identity alone survives re-insertion.
        foreach (var pair in _nodes)
            if (!_seen.Contains(pair.Key)) { pair.Value.Exposed = false; pair.Value.Revision++; _removed.Add(pair.Value); }
        foreach (var ax in _removed) { _nodes.Remove(ax.Source); ClearDependencies(ax.Source); _dependents.Remove(ax.Source); }
        // aria-owns changes the AX parent, never DOM event/hit/focus ownership. Ignore cycles and repeated ownership.
        _owned.Clear(); _ownershipOrder.Clear();
        foreach (var ax in _ordered)
            if (ax.Source is Element e)
                foreach (var id in AccessibleRoles.Tokens(e.GetAttribute("aria-owns")))
                    if (Resolve(e, id) is { } target && Get(target) is { } child && !ReferenceEquals(child, ax)
                        && !IsAncestor(child, ax) && _owned.Add(child))
                    { child.PlannedParent = ax; _ownershipOrder.Add(child); }
        foreach (var ax in _ordered)
        {
            ax.PlannedChildCount = 0;
            if (!ReferenceEquals(ax.Parent, ax.PlannedParent)) { ax.Parent = ax.PlannedParent; ChangedNode(ax); }
        }
        foreach (var ax in _ordered)
            if (ax.Parent is { } parent && !_owned.Contains(ax)) PlaceChild(parent, ax);
        foreach (var ax in _ownershipOrder) PlaceChild(ax.Parent!, ax);
        foreach (var ax in _ordered)
        {
            if (ax.MutableChildren.Count > ax.PlannedChildCount)
            {
                ax.MutableChildren.RemoveRange(ax.PlannedChildCount, ax.MutableChildren.Count - ax.PlannedChildCount);
                ChangedNode(ax);
            }
            _dirty.Add(ax.Source);
        }
    }
    private static bool IsAncestor(AccessibleNode ancestor, AccessibleNode node)
    { for (var n = node.PlannedParent; n != null; n = n.PlannedParent) if (ReferenceEquals(n, ancestor)) return true; return false; }
    private void PlaceChild(AccessibleNode parent, AccessibleNode child)
    {
        int index = parent.PlannedChildCount++;
        if (index == parent.MutableChildren.Count) { parent.MutableChildren.Add(child); ChangedNode(parent); }
        else if (!ReferenceEquals(parent.MutableChildren[index], child)) { parent.MutableChildren[index] = child; ChangedNode(parent); }
    }
    private void Visit(Node source, AccessibleNode? parent, bool excluded, bool presentational)
    {
        if (source is Element e)
        {
            excluded |= Excluded(e);
            if (e.LocalName is "head" or "script" or "style" or "template" or "meta" or "link" or "title") excluded = true;
            if (e is HTMLInputElement { Type: "hidden" }) excluded = true;
        }
        if (excluded) return;
        if (source is not (Document or Element or Text)) { foreach (var child in ComposedChildren(source)) Visit(child, parent, false, presentational); return; }
        if (source is Text text && (string.IsNullOrWhiteSpace(text.Data) || text.ParentElement is { } textParent && (_renderHidden?.Invoke(textParent) ?? false))) return;
        string role = source is Element element ? AccessibleRoles.Compute(element, Focusable(element)) : source is Text ? "StaticText" : "RootWebArea";
        bool ignored = role == "none" || source is Element ie && (ie.LocalName is "html" or "body" || presentational && !Focusable(ie) || (_renderHidden?.Invoke(ie) ?? false))
            || AssociatedLabelContent(source);
        var ax = Identity(source); _nodes[source] = ax; _seen.Add(source); _ordered.Add(ax);
        if (ax.Ignored != ignored) { ax.Ignored = ignored; ChangedNode(ax); }
        ax.PlannedParent = parent; ax.Exposed = true;
        parent = ax;
        // Required-owned descendants inherit presentation only for table/list structure, not arbitrary contents.
        bool inherit = source is Element pe && ignored && pe.LocalName is "table" or "thead" or "tbody" or "tfoot" or "tr" or "ul" or "ol" or "li";
        foreach (var child in ComposedChildren(source)) Visit(child, parent, false, inherit);
    }
    private bool AssociatedLabelContent(Node source)
    {
        if (source is Element e && Focusable(e)) return false;
        for (var n = source; n != null; n = n.ParentNode)
            if (n is Element { LocalName: "label" } label && LabelControl(label) != null) return true;
        return false;
    }
    internal static IEnumerable<Node> ComposedChildren(Node source)
    {
        if (source is Element { ShadowRootInternal: { } shadow }) source = shadow;
        if (source is HTMLSlotElement { Assigned.Count: > 0 } slot)
        { foreach (var child in slot.Assigned) yield return child; yield break; }
        for (var n = source.FirstChild; n != null; n = n.NextSibling) yield return n;
    }
    private bool Excluded(Element e) => e.HasAttribute("hidden") || Document.IsInert(e) || e.GetAttribute("aria-hidden") == "true" || (_renderSubtreeHidden?.Invoke(e) ?? false)
        || e.LocalName == "dialog" && !e.HasAttribute("open");
    private bool Hidden(Node source)
    {
        if (source is Element current && (_renderHidden?.Invoke(current) ?? false)) return true;
        if (source is Text { ParentElement: { } parent } && (_renderHidden?.Invoke(parent) ?? false)) return true;
        for (Node? n = source; n != null; n = n.AssignedSlotInternal ?? n.ParentNode ?? (n is ShadowRoot sr ? sr.Host : null))
        { Depend(n); if (n is Element e && Excluded(e)) return true; }
        return false;
    }
    private bool Focusable(Element e) => e is HTMLElement html && _document.IsFocusCandidate(html);
    private Element? Resolve(Element from, string id)
    {
        if (_idReferences.TryGetValue(id, out var elements))
            foreach (var e in elements) if (ReferenceEquals(e.Root, from.Root)) return e;
        return null;
    }
    private void Depend(Node source)
    {
        if (_computing == null || ReferenceEquals(_computing, source)) return;
        if (!_dependents.TryGetValue(source, out var users)) _dependents[source] = users = new();
        users.Add(_computing);
        if (!_dependencies.TryGetValue(_computing, out var sources)) _dependencies[_computing] = sources = new();
        sources.Add(source);
    }
    private void ClearDependencies(Node user)
    {
        if (!_dependencies.Remove(user, out var sources)) return;
        foreach (var source in sources) if (_dependents.TryGetValue(source, out var users)) { users.Remove(user); if (users.Count == 0) _dependents.Remove(source); }
    }
    private void Mark(Node source)
    {
        _dirty.Add(source);
        if (_dependents.TryGetValue(source, out var users)) foreach (var user in users) _dirty.Add(user);
        for (var p = source.ParentNode; p != null; p = p.ParentNode) _dirty.Add(p);
    }
    private void ChangedNode(AccessibleNode ax) { if (!_changed.Contains(ax)) { ax.Revision++; _changed.Add(ax); } }
    bool IDomChangeSink.RequiresAttributeOldValue => _previous?.RequiresAttributeOldValue == true;
    void IDomChangeSink.ChildrenChanged(Node n) { _previous?.ChildrenChanged(n); _structureDirty = true; Mark(n); }
    void IDomChangeSink.NodeInserted(Node n) { _previous?.NodeInserted(n); _structureDirty = true; }
    void IDomChangeSink.NodeRemoved(Node n, Node parent) { _previous?.NodeRemoved(n, parent); _structureDirty = true; Mark(parent); }
    void IDomChangeSink.CharacterDataChanged(CharacterData n)
    {
        _previous?.CharacterDataChanged(n); Mark(n);
        if (n is Text && string.IsNullOrWhiteSpace(n.Data) != (_nodes.GetValueOrDefault(n) == null)) _structureDirty = true;
    }
    void IDomChangeSink.AttributeChanged(Element e, string name, string? old)
    {
        _previous?.AttributeChanged(e, name, old); Mark(e);
        if (name is "id" or "role" or "slot" or "name" or "hidden" or "inert" or "aria-hidden" or "aria-owns" or "style" or "class" or "open" or "tabindex" or "type") _structureDirty = true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (!ReferenceEquals(_document.ChangeSink, this)) throw new InvalidOperationException("Accessibility projection must be detached before a later DOM sink.");
        _document.ChangeSink = _previous; _disposed = true;
        foreach (var node in _ordered) { node.Exposed = false; node.Parent = null; node.MutableChildren.Clear(); }
        _nodes.Clear(); _ordered.Clear(); _changed.Clear(); _removed.Clear(); _dependents.Clear(); _dependencies.Clear(); _idReferences.Clear(); _dirty.Clear(); _seen.Clear();
        _owned.Clear(); _ownershipOrder.Clear();
        Root.MutableChildren.Clear(); Root.Exposed = false;
    }
}
