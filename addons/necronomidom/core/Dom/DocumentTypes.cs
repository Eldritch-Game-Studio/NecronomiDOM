using System;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.5 DOMImplementation document creation.</summary>
    public sealed class DOMImplementation
    {
        private readonly Document _document;
        internal DOMImplementation(Document document) => _document = document;
        public bool HasFeature() => true;
        public DocumentType CreateDocumentType(string qualifiedName, string publicId = "", string systemId = "")
        {
            Names.Validate(qualifiedName);
            var colon = qualifiedName.IndexOf(':');
            if (colon == 0 || colon == qualifiedName.Length - 1 || colon >= 0 && qualifiedName.IndexOf(':', colon + 1) >= 0)
                throw DomException.InvalidCharacter("Invalid qualified name.");
            if (colon >= 0 && !Names.IsXmlName(qualifiedName.AsSpan(colon + 1), false))
                throw DomException.InvalidCharacter("Invalid qualified name local part.");
            return new DocumentType(_document, qualifiedName, publicId, systemId);
        }
        public Document CreateDocument(string? ns, string qualifiedName, DocumentType? doctype = null)
        {
            var doc = new Document();
            if (doctype != null) doc.AppendChild(doctype);
            if (qualifiedName.Length > 0) doc.AppendChild(doc.CreateElementNS(ns, qualifiedName));
            return doc;
        }
        public Document CreateHTMLDocument(string? title = null)
        {
            var doc = new Document { IsHtmlDocument = true };
            doc.AppendChild(new DocumentType(doc, "html", "", ""));
            var html = doc.CreateElement("html"); doc.AppendChild(html);
            var head = doc.CreateElement("head"); html.AppendChild(head);
            if (title != null) { var t = doc.CreateElement("title"); t.TextContent = title; head.AppendChild(t); }
            html.AppendChild(doc.CreateElement("body")); return doc;
        }
    }
    /// <summary>DOM § 4.6 DocumentType node.</summary>
    public sealed partial class DocumentType : Node
    {
        internal DocumentType(Document doc, string name, string publicId, string systemId) : base(doc)
        { Name = name; PublicId = publicId; SystemId = systemId; }
        public string Name { get; }
        public string PublicId { get; }
        public string SystemId { get; }
        public override ushort NodeType => DocumentTypeNode;
        public override string NodeName => Name;
    }
    /// <summary>DOM § 4.7 DocumentFragment, including inert template contents.</summary>
    public partial class DocumentFragment : Node
    {
        internal DocumentFragment(Document doc) : base(doc) { }
        internal Element? HostInternal { get; set; }
        public override ushort NodeType => DocumentFragmentNode;
        public override string NodeName => "#document-fragment";
        public Element? GetElementById(string id)
        { foreach (var n in InclusiveDescendants()) if (n is Element e && e.Id == id && id.Length > 0) return e; return null; }
    }
    /// <summary>DOM § 4.8 ShadowRoot mode.</summary>
    public enum ShadowRootMode { Open, Closed }
    /// <summary>DOM § 4.8 slot assignment mode.</summary>
    public enum SlotAssignmentMode { Named, Manual }
    /// <summary>DOM § 4.8 ShadowRootInit.</summary>
    public sealed class ShadowRootInit
    {
        public ShadowRootMode Mode { get; init; }
        public bool DelegatesFocus { get; init; }
        public SlotAssignmentMode SlotAssignment { get; init; }
        public bool Clonable { get; init; }
        public bool Serializable { get; init; }
    }
    /// <summary>DOM § 4.8 shadow tree root and HTML declarative shadow state.</summary>
    public sealed partial class ShadowRoot : DocumentFragment
    {
        internal ShadowRoot(Element host, ShadowRootInit init) : base(host.NodeDocument)
        { Host = host; HostInternal = host; Mode = init.Mode; DelegatesFocus = init.DelegatesFocus; SlotAssignment = init.SlotAssignment; Clonable = init.Clonable; Serializable = init.Serializable; }
        public Element Host { get; }
        public ShadowRootMode Mode { get; }
        public bool DelegatesFocus { get; }
        public SlotAssignmentMode SlotAssignment { get; }
        public bool Clonable { get; }
        public bool Serializable { get; }
        internal bool Declarative { get; set; }
        internal override EventTarget? GetTheParent(Event e) => !e.Composed && e.Path.Count > 0 && e.Path[0].InvocationTarget is Node n && ReferenceEquals(n.Root, this) ? null : Host;
    }
}
