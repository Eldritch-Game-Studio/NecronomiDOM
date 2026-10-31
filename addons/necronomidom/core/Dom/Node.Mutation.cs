using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    public abstract partial class Node
    {
        public Node AppendChild(Node node) => InsertBefore(node, null);
        public Node InsertBefore(Node node, Node? child)
        {
            using var reactions = NodeDocument.Reactions();
            using var sourceReactions = node.NodeDocument.Reactions();
            ValidateInsertion(node, child, null);
            if (ReferenceEquals(node, child)) child = child!.NextSibling;
            Insert(node, child); return node;
        }
        public Node ReplaceChild(Node node, Node child)
        {
            using var reactions = NodeDocument.Reactions();
            using var sourceReactions = node.NodeDocument.Reactions();
            ValidateInsertion(node, child, child);
            var next = child.NextSibling;
            if (ReferenceEquals(next, node)) next = node.NextSibling;
            var previous = child.PreviousSibling;
            node.ParentNode?.RemoveChild(node);
            IReadOnlyList<Node> removed = Array.Empty<Node>();
            if (ReferenceEquals(child.ParentNode, this)) { Remove(child, false); removed = new[] { child }; }
            var added = InsertionNodes(node);
            Insert(node, next, false);
            MutationObserver.Queue(this, "childList", added: added, removed: removed, previous: previous, next: next);
            return child;
        }
        public Node RemoveChild(Node child)
        {
            using var reactions = NodeDocument.Reactions();
            if (!ReferenceEquals(child.ParentNode, this)) throw DomException.NotFound("Node is not a child.");
            Remove(child); return child;
        }
        internal void ValidateInsertion(Node node, Node? child, Node? replacing, bool replacingAll = false)
        {
            if (this is not (Document or DocumentFragment or Element)) throw DomException.HierarchyRequest("Parent cannot have children.");
            if (node.IsHostIncludingInclusiveAncestorOf(this)) throw DomException.HierarchyRequest("Insertion would create a cycle.");
            if (child != null && !ReferenceEquals(child.ParentNode, this)) throw DomException.NotFound("Reference node is not a child.");
            if (node is not (DocumentFragment or DocumentType or Element or CharacterData) || node is ShadowRoot)
                throw DomException.HierarchyRequest("This node cannot be inserted.");
            if (node is DocumentType && this is not Document || node is Text && this is Document)
                throw DomException.HierarchyRequest("Invalid document child.");
            if (this is not Document) return;
            var final = new List<Node>();
            if (!replacingAll) foreach (var n in ChildNodes) if (!ReferenceEquals(n, node) && !ReferenceEquals(n, replacing)) final.Add(n);
            var reference = ReferenceEquals(node, child) ? child?.NextSibling : child;
            var position = reference == null ? final.Count : final.IndexOf(reference);
            if (position < 0) position = replacing?.NextSibling is { } next ? final.IndexOf(next) : final.Count;
            if (position < 0) position = final.Count;
            if (node is DocumentFragment) { foreach (var n in node.ChildNodes) final.Insert(position++, n); }
            else final.Insert(position, node);
            var element = -1; var doctype = -1;
            for (var i = 0; i < final.Count; i++)
            {
                if (final[i] is Text) throw DomException.HierarchyRequest("Document cannot contain text.");
                if (final[i] is Element) { if (element >= 0) throw DomException.HierarchyRequest("Multiple document elements."); element = i; }
                if (final[i] is DocumentType) { if (doctype >= 0) throw DomException.HierarchyRequest("Multiple doctypes."); doctype = i; }
            }
            if (doctype >= 0 && element >= 0 && doctype > element) throw DomException.HierarchyRequest("Doctype must precede document element.");
        }
        private static IReadOnlyList<Node> InsertionNodes(Node node)
        {
            if (node is not DocumentFragment) return new[] { node };
            var nodes = new List<Node>(node.ChildCount); foreach (var n in node.ChildNodes) nodes.Add(n); return nodes;
        }
        internal void Insert(Node node, Node? child, bool notify = true)
        {
            var nodes = InsertionNodes(node);
            if (nodes.Count == 0) return;
            if (node is DocumentFragment)
            {
                foreach (var n in nodes) node.Remove(n, false);
                if (nodes.Count > 0) MutationObserver.Queue(node, "childList", removed: nodes);
            }
            else node.ParentNode?.RemoveChild(node);
            var previous = child?.PreviousSibling ?? (child == null ? LastChild : null);
            foreach (var n in nodes)
            {
                n.AdoptInto(NodeDocument);
                n.ParentInternal = this; n.PreviousSiblingInternal = previous; n.NextSiblingInternal = child;
                if (previous == null) FirstChildInternal = n; else previous.NextSiblingInternal = n;
                if (child == null) LastChildInternal = n; else child.PreviousSiblingInternal = n;
                ChildCount++; previous = n;
                NodeDocument.ChangeSink?.NodeInserted(n);
                foreach (var descendant in n.ShadowIncludingInclusiveDescendants())
                    if (descendant is Element e)
                    {
                        var wasDefined = e.CustomDefinition != null;
                        NodeDocument.CustomElements.TryUpgrade(e);
                        if (wasDefined && e.IsConnected) e.CustomDefinition?.Connected(e);
                        if (e is HTMLSelectElement select) select.RefreshSelectedContent(null);
                    }
            }
            if (notify && nodes.Count > 0) MutationObserver.Queue(this, "childList", added: nodes, previous: nodes[0].PreviousSibling, next: child);
            NodeDocument.ChangeSink?.ChildrenChanged(this); SlotDistribution.Refresh(this);
            SelectedContentProjection.Refresh(this);
            if (this is HTMLSlotElement { Assigned.Count: 0 } slot && slot.Root is ShadowRoot) NodeDocument.PendingSlots.Add(slot);
        }
        internal void Remove(Node child, bool notify = true)
        {
            var connected = child.IsConnected;
            NodeDocument.FocusedSubtreeRemoved(child);
            NodeDocument.PointerInput?.PointerSubtreeRemoved(child);
            var previous = child.PreviousSibling; var next = child.NextSibling;
            MutationObserver.AddTransientObservers(this, child);
            if (previous == null) FirstChildInternal = next; else previous.NextSiblingInternal = next;
            if (next == null) LastChildInternal = previous; else next.PreviousSiblingInternal = previous;
            child.ParentInternal = null; child.PreviousSiblingInternal = null; child.NextSiblingInternal = null; ChildCount--;
            if (notify) MutationObserver.Queue(this, "childList", removed: new[] { child }, previous: previous, next: next);
            NodeDocument.ChangeSink?.NodeRemoved(child, this); NodeDocument.ChangeSink?.ChildrenChanged(this);
            if (connected) foreach (var n in child.ShadowIncludingInclusiveDescendants()) if (n is Element e) e.CustomDefinition?.Disconnected(e);
            foreach (var n in child.InclusiveDescendants())
                if (n is HTMLSlotElement slot && slot.Assigned.Count > 0)
                {
                    foreach (var assigned in slot.Assigned) assigned.AssignedSlotInternal = null;
                    slot.Assigned.Clear(); NodeDocument.PendingSlots.Add(slot);
                }
            child.AssignedSlotInternal = null; SlotDistribution.Refresh(this);
            SelectedContentProjection.Refresh(this);
            if (this is HTMLSlotElement { Assigned.Count: 0 } fallbackSlot && fallbackSlot.Root is ShadowRoot) NodeDocument.PendingSlots.Add(fallbackSlot);
        }
        internal void AdoptInto(Document document)
        {
            if (ReferenceEquals(NodeDocument, document)) return;
            var old = NodeDocument;
            foreach (var n in ShadowIncludingInclusiveDescendants())
            {
                n.NodeDocument = document;
                if (n is Element e)
                {
                    foreach (var a in e.AttributeList) a.NodeDocument = document;
                    e.CustomDefinition?.Adopted(e, old, document);
                    if (e is HTMLTemplateElement template) template.Content.AdoptInto(document.TemplateDocument);
                }
            }
        }
        internal override EventTarget? GetTheParent(Event e) => AssignedSlotInternal ?? (EventTarget?)ParentNode;
    }
}
