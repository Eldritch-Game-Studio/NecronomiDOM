using System;
using System.Text;

namespace NecronomiDom.Dom
{
    public abstract partial class Node
    {
        public virtual string? NodeValue
        {
            get => this is Attr a ? a.Value : this is CharacterData c ? c.Data : null;
            set { if (this is Attr a) a.Value = value ?? ""; else if (this is CharacterData c) c.Data = value ?? ""; }
        }
        public virtual string? TextContent
        {
            get
            {
                if (this is Attr or CharacterData) return NodeValue;
                if (this is Document or DocumentType) return null;
                var b = new StringBuilder(); foreach (var n in InclusiveDescendants()) if (n is Text t) b.Append(t.Data); return b.ToString();
            }
            set
            {
                if (this is Attr or CharacterData) { NodeValue = value; return; }
                if (this is Element or DocumentFragment) ReplaceChildren(value == null || value.Length == 0 ? Array.Empty<NodeOrString>() : new NodeOrString[] { value });
            }
        }
        public void Normalize()
        {
            using var reactions = NodeDocument.Reactions();
            for (var n = FirstChild; n != null;)
            {
                var next = n.NextSibling;
                if (n is Text text && n is not CDATASection)
                {
                    if (text.Length == 0) RemoveChild(text);
                    else
                    {
                        var end = next;
                        StringBuilder? data = null;
                        while (end is Text adjacent && end is not CDATASection)
                        { (data ??= new StringBuilder()).Append(adjacent.Data); end = end.NextSibling; }
                        text.AppendData(data?.ToString() ?? string.Empty);
                        while (!ReferenceEquals(next, end))
                        { var removed = next!; next = removed.NextSibling; RemoveChild(removed); }
                    }
                }
                else n.Normalize();
                n = next;
            }
        }
        public ushort CompareDocumentPosition(Node other)
        {
            if (ReferenceEquals(this, other)) return 0;
            Node a = this is Attr { OwnerElement: { } ao } ? ao : this;
            Node b = other is Attr { OwnerElement: { } bo } ? bo : other;
            if (ReferenceEquals(a, b) && this is Attr aa && other is Attr ba)
                return (ushort)(DocumentPositionImplementationSpecific | (aa.OwnerElement!.AttributeList.IndexOf(aa) < aa.OwnerElement.AttributeList.IndexOf(ba) ? DocumentPositionFollowing : DocumentPositionPreceding));
            if (ReferenceEquals(a, b)) return this is Attr
                ? (ushort)(DocumentPositionPreceding | DocumentPositionContains)
                : (ushort)(DocumentPositionFollowing | DocumentPositionContainedBy);
            if (!ReferenceEquals(a.Root, b.Root)) return (ushort)(DocumentPositionDisconnected | DocumentPositionImplementationSpecific | (_identity < other._identity ? DocumentPositionFollowing : DocumentPositionPreceding));
            if (a.Contains(b)) return (ushort)(DocumentPositionFollowing | DocumentPositionContainedBy);
            if (b.Contains(a)) return (ushort)(DocumentPositionPreceding | DocumentPositionContains);
            foreach (var n in a.Root.InclusiveDescendants()) { if (ReferenceEquals(n, a)) return DocumentPositionFollowing; if (ReferenceEquals(n, b)) return DocumentPositionPreceding; }
            return 0;
        }
        private Element? NamespaceElement => this is Element e ? e : this is Document d ? d.DocumentElement : this is Attr a ? a.OwnerElement : ParentElement;
        public string? LookupNamespaceURI(string? prefix)
        {
            prefix = string.IsNullOrEmpty(prefix) ? null : prefix;
            var e = NamespaceElement; if (e == null) return null;
            if (prefix == "xml") return Names.Xml;
            if (prefix == "xmlns") return Names.Xmlns;
            if (e.NamespaceURI != null && e.Prefix == prefix) return e.NamespaceURI;
            foreach (var a in e.AttributeList)
                if (a.NamespaceURI == Names.Xmlns && (prefix == null && a.Name == "xmlns" || a.Prefix == "xmlns" && a.LocalName == prefix)) return a.Value.Length == 0 ? null : a.Value;
            return e.ParentElement?.LookupNamespaceURI(prefix);
        }
        public string? LookupPrefix(string? ns)
        {
            if (string.IsNullOrEmpty(ns)) return null;
            var e = NamespaceElement; if (e == null) return null;
            if (e.NamespaceURI == ns && e.Prefix != null) return e.Prefix;
            foreach (var a in e.AttributeList) if (a.Prefix == "xmlns" && a.Value == ns) return a.LocalName;
            return e.ParentElement?.LookupPrefix(ns);
        }
        public bool IsDefaultNamespace(string? ns) => LookupNamespaceURI(null) == (string.IsNullOrEmpty(ns) ? null : ns);
    }
}
