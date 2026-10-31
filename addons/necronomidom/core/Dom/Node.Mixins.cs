using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    public abstract partial class Node
    {
        private HTMLCollection? _children;
        internal HTMLCollection Children => _children ??= new HTMLCollection(this, _ => true, true);
        internal Element? FirstElementChild { get { for (var n = FirstChild; n != null; n = n.NextSibling) if (n is Element e) return e; return null; } }
        internal Element? LastElementChild { get { for (var n = LastChild; n != null; n = n.PreviousSibling) if (n is Element e) return e; return null; } }
        internal int ChildElementCount => Children.Length;
        internal Element? PreviousElementSibling { get { for (var n = PreviousSibling; n != null; n = n.PreviousSibling) if (n is Element e) return e; return null; } }
        internal Element? NextElementSibling { get { for (var n = NextSibling; n != null; n = n.NextSibling) if (n is Element e) return e; return null; } }
        internal Node ConvertNodes(NodeOrString[] nodes)
        {
            if (nodes.Length == 1) return nodes[0].Resolve(NodeDocument);
            var fragment = NodeDocument.CreateDocumentFragment();
            foreach (var node in nodes) fragment.AppendChild(node.Resolve(NodeDocument));
            return fragment;
        }
        internal void Append(params NodeOrString[] nodes)
        { using var reactions = NodeDocument.Reactions(); AppendChild(ConvertNodes(nodes)); }
        internal void Prepend(params NodeOrString[] nodes)
        { using var reactions = NodeDocument.Reactions(); InsertBefore(ConvertNodes(nodes), FirstChild); }
        internal void ReplaceChildren(params NodeOrString[] nodes)
        {
            using var reactions = NodeDocument.Reactions();
            ReplaceChildrenCore(ConvertNodes(nodes));
        }
        internal void ReplaceAllChildren(Node node)
        {
            using var reactions = NodeDocument.Reactions();
            ReplaceChildrenCore(node);
        }
        private void ReplaceChildrenCore(Node node)
        {
            ValidateInsertion(node, null, null, true);
            var removed = new List<Node>(ChildCount); foreach (var child in ChildNodes) removed.Add(child);
            foreach (var child in removed) Remove(child, false);
            var added = InsertionNodes(node); Insert(node, null, false);
            if (removed.Count > 0 || added.Count > 0) MutationObserver.Queue(this, "childList", added: added, removed: removed);
        }
        internal void Remove() => ParentNode?.RemoveChild(this);
        private static bool IsArgument(Node candidate, NodeOrString[] nodes, Document document)
        { foreach (var node in nodes) if (node.IsNode(candidate)) return true; return false; }
        internal void Before(params NodeOrString[] nodes)
        {
            using var reactions = NodeDocument.Reactions();
            var parent = ParentNode; if (parent == null) return;
            var previous = PreviousSibling;
            while (previous != null && IsArgument(previous, nodes, NodeDocument)) previous = previous.PreviousSibling;
            var node = ConvertNodes(nodes); parent.InsertBefore(node, previous?.NextSibling ?? parent.FirstChild);
        }
        internal void After(params NodeOrString[] nodes)
        {
            using var reactions = NodeDocument.Reactions();
            var parent = ParentNode; if (parent == null) return;
            var next = NextSibling;
            while (next != null && IsArgument(next, nodes, NodeDocument)) next = next.NextSibling;
            parent.InsertBefore(ConvertNodes(nodes), next);
        }
        internal void ReplaceWith(params NodeOrString[] nodes)
        {
            using var reactions = NodeDocument.Reactions();
            var parent = ParentNode; if (parent == null) return;
            var next = NextSibling;
            while (next != null && IsArgument(next, nodes, NodeDocument)) next = next.NextSibling;
            var node = ConvertNodes(nodes);
            if (ReferenceEquals(ParentNode, parent)) parent.ReplaceChild(node, this); else parent.InsertBefore(node, next);
        }
        internal HTMLCollection GetElementsByTagName(string name)
        {
            var folded = Names.AsciiLower(name);
            return new HTMLCollection(this, e => name == "*" || (NodeDocument.IsHtmlDocument && e.NamespaceURI == Names.Html ? e.LocalName == folded : e.QualifiedName == name));
        }
        internal HTMLCollection GetElementsByTagNameNS(string? ns, string local)
        { ns = string.IsNullOrEmpty(ns) ? null : ns; return new HTMLCollection(this, e => (ns == "*" || e.NamespaceURI == ns) && (local == "*" || e.LocalName == local)); }
        internal HTMLCollection GetElementsByClassName(string names)
        {
            var tokens = DOMTokenList.Parse(names);
            return new HTMLCollection(this, e =>
            {
                if (tokens.Count == 0) return false;
                foreach (var token in tokens)
                {
                    if (NodeDocument.CompatMode == "BackCompat")
                    { var found = false; foreach (var c in e.ClassList) if (Names.AsciiEqual(c, token)) found = true; if (!found) return false; }
                    else if (!e.ClassList.Contains(token)) return false;
                }
                return true;
            });
        }
    }
}
