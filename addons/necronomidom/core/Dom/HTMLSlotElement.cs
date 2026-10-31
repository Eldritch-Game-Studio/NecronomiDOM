using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4.12.4 slot assignment, flattening and manual assignment.</summary>
    public sealed class HTMLSlotElement : HTMLElement
    {
        internal readonly List<Node> Assigned = new();
        internal readonly List<Node> Manual = new();
        internal HTMLSlotElement(Document doc) : base(doc, "slot") { }
        public string Name { get => GetAttribute("name") ?? ""; set => SetAttribute("name", value); }
        public IReadOnlyList<Node> AssignedNodes(AssignedNodesOptions? options = null)
        {
            if (options?.Flatten != true) return Assigned.ToArray();
            var result = new List<Node>(); Flatten(this, result, new HashSet<HTMLSlotElement>()); return result;
        }
        private static void Flatten(HTMLSlotElement slot, List<Node> result, HashSet<HTMLSlotElement> active)
        {
            if (!active.Add(slot)) return;
            if (slot.Assigned.Count > 0)
            { foreach (var n in slot.Assigned) if (n is HTMLSlotElement nested && nested.Root is ShadowRoot) Flatten(nested, result, active); else result.Add(n); }
            else foreach (var n in slot.ChildNodes) if (n is HTMLSlotElement nested && nested.Root is ShadowRoot) Flatten(nested, result, active); else if (n is Element or Text) result.Add(n);
            active.Remove(slot);
        }
        public IReadOnlyList<Element> AssignedElements(AssignedNodesOptions? options = null)
        { var result = new List<Element>(); foreach (var n in AssignedNodes(options)) if (n is Element e) result.Add(e); return result; }
        public void Assign(params Node[] nodes)
        {
            foreach (var n in nodes) if (n is not (Element or Text)) throw new DomTypeError("Only elements and text are slottable.");
            // Compare the de-duplicated manual sequence without allocating another copy.
            bool changed = false; int count = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                bool duplicate = false;
                for (int j = 0; j < i; j++) if (ReferenceEquals(nodes[i], nodes[j])) { duplicate = true; break; }
                if (duplicate) continue;
                changed |= count >= Manual.Count || !ReferenceEquals(Manual[count], nodes[i]); count++;
            }
            changed |= count != Manual.Count;
            foreach (var n in Manual) if (ReferenceEquals(n.ManualSlotAssignment, this)) n.ManualSlotAssignment = null;
            Manual.Clear();
            foreach (var n in nodes)
            {
                if (Manual.Contains(n)) continue;
                n.ManualSlotAssignment?.Manual.Remove(n); n.ManualSlotAssignment = this; Manual.Add(n);
            }
            SlotDistribution.Refresh(this);
            if (changed) NodeDocument.ChangeSink?.ChildrenChanged(this);
        }
    }
    /// <summary>HTML slot assignedNodes/assignedElements flatten option.</summary>
    public sealed class AssignedNodesOptions { public bool Flatten { get; init; } }
    internal static class SlotDistribution
    {
        internal static void Refresh(Node changed)
        {
            var root = changed.ShadowIncludingRoot;
            foreach (var n in root.ShadowIncludingInclusiveDescendants())
                if (n is ShadowRoot shadow) Distribute(shadow);
        }
        private static void Distribute(ShadowRoot root)
        {
            var slots = new List<HTMLSlotElement>();
            foreach (var n in root.InclusiveDescendants()) if (n is HTMLSlotElement slot) slots.Add(slot);
            foreach (var child in root.Host.ChildNodes) child.AssignedSlotInternal = null;
            foreach (var slot in slots)
            {
                var assigned = new List<Node>();
                if (root.SlotAssignment == SlotAssignmentMode.Manual)
                { foreach (var child in slot.Manual) if (ReferenceEquals(child.ParentNode, root.Host)) assigned.Add(child); }
                else foreach (var child in root.Host.ChildNodes)
                {
                    if (child is not (Element or Text) || child.AssignedSlotInternal != null) continue;
                    if ((child is Element e ? e.Slot : "") == slot.Name) assigned.Add(child);
                }
                foreach (var child in assigned) child.AssignedSlotInternal = slot;
                var changed = assigned.Count != slot.Assigned.Count;
                if (!changed) for (var i = 0; i < assigned.Count; i++) if (!ReferenceEquals(assigned[i], slot.Assigned[i])) { changed = true; break; }
                slot.Assigned.Clear(); slot.Assigned.AddRange(assigned);
                if (changed) root.NodeDocument.PendingSlots.Add(slot);
            }
        }
    }
}
