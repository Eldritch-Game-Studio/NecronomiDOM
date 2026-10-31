using System;
using System.Collections;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.2.10 NodeList, with live child and static snapshot implementations.</summary>
    public abstract class NodeList : IEnumerable<Node>
    {
        public abstract int Length { get; }
        public abstract Node? Item(int index);
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public Node? this[int index] => Item(index);
        public IEnumerator<Node> GetEnumerator() { for (var i = 0; i < Length; i++) yield return Item(i)!; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal sealed class ChildNodeList : NodeList
    {
        private readonly Node _parent;
        internal ChildNodeList(Node parent) => _parent = parent;
        public override int Length => _parent.ChildCount;
        public override Node? Item(int index)
        {
            if (index < 0) return null;
            var n = _parent.FirstChild;
            while (index-- > 0 && n != null) n = n.NextSibling;
            return n;
        }
    }
    internal sealed class StaticNodeList : NodeList
    {
        private readonly IReadOnlyList<Node> _nodes;
        internal StaticNodeList(IReadOnlyList<Node> nodes) => _nodes = nodes;
        public override int Length => _nodes.Count;
        public override Node? Item(int index) => index >= 0 && index < Length ? _nodes[index] : null;
    }
    /// <summary>DOM § 4.2.10 live filtered element collection in tree order.</summary>
    public sealed class HTMLCollection : IEnumerable<Element>
    {
        private readonly Node _root;
        private readonly Func<Element, bool> _filter;
        private readonly bool _direct;
        internal HTMLCollection(Node root, Func<Element, bool> filter, bool direct = false)
        { _root = root; _filter = filter; _direct = direct; }
        public int Length { get { var count = 0; foreach (var e in this) count++; return count; } }
        public Element? Item(int index)
        { if (index < 0) return null; foreach (var e in this) if (index-- == 0) return e; return null; }
        public Element? NamedItem(string name)
        {
            if (name.Length == 0) return null;
            foreach (var e in this) if (e.Id == name || e.NamespaceURI == Names.Html && e.GetAttribute("name") == name) return e;
            return null;
        }
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public Element? this[int index] => Item(index);
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public Element? this[string name] => NamedItem(name);
        public IEnumerator<Element> GetEnumerator()
        {
            for (var n = _root.FirstChild; n != null; n = _direct ? n.NextSibling : n.NextInTreeOrder(_root))
                if (n is Element e && _filter(e)) yield return e;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    /// <summary>DOM § 4.9 NamedNodeMap, live ordered attribute access.</summary>
    public sealed class NamedNodeMap : IEnumerable<Attr>
    {
        private readonly Element _element;
        internal NamedNodeMap(Element element) => _element = element;
        public int Length => _element.AttributeList.Count;
        public Attr? Item(int i) => i >= 0 && i < Length ? _element.AttributeList[i] : null;
        [System.Runtime.CompilerServices.IndexerName("Indexed")]
        public Attr? this[int i] => Item(i);
        public Attr? GetNamedItem(string name) => _element.GetAttributeNode(name);
        public Attr? GetNamedItemNS(string? ns, string local) => _element.GetAttributeNodeNS(ns, local);
        public Attr? SetNamedItem(Attr attr) => _element.SetAttributeNode(attr);
        public Attr? SetNamedItemNS(Attr attr) => _element.SetAttributeNodeNS(attr);
        public Attr RemoveNamedItem(string name) => _element.RemoveAttributeNode(GetNamedItem(name) ?? throw DomException.NotFound(name));
        public Attr RemoveNamedItemNS(string? ns, string local) => _element.RemoveAttributeNode(GetNamedItemNS(ns, local) ?? throw DomException.NotFound(local));
        public IEnumerator<Attr> GetEnumerator() => _element.AttributeList.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    /// <summary>DOM ParentNode/ChildNode variadic node-or-string argument.</summary>
    public readonly struct NodeOrString
    {
        private readonly Node? _node;
        private readonly string? _text;
        private NodeOrString(Node? node, string? text) { _node = node; _text = text; }
        internal bool IsNode(Node node) => ReferenceEquals(_node, node);
        internal Node Resolve(Document document) => _node ?? document.CreateTextNode(_text ?? string.Empty);
        public static implicit operator NodeOrString(Node node) => new(node, null);
        public static implicit operator NodeOrString(string text) => new(null, text);
    }
}
