using System;
using NecronomiDom.Url;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.5 Document and HTML document metadata and node factories.</summary>
    public partial class Document : Node
    {
        private Document? _templateDocument;
        public Document() : base(null) { Implementation = new DOMImplementation(this); CustomElements = new CustomElementRegistry(this); }
        public override ushort NodeType => DocumentNode;
        public override string NodeName => "#document";
        public DOMImplementation Implementation { get; }
        public CustomElementRegistry CustomElements { get; }
        internal IDomChangeSink? ChangeSink { get; set; }
        public Action<Exception>? ListenerExceptionHandler { get; set; }
        public WebUrl? BaseUrl { get; set; }
        internal string DocumentBaseUrlHref => BaseUrl?.Href ?? "about:blank";
        public string URL => DocumentBaseUrlHref;
        public string DocumentURI => URL;
        public string CompatMode { get; internal set; } = "CSS1Compat";
        public string CharacterSet => "UTF-8";
        public string Charset => CharacterSet;
        public string InputEncoding => CharacterSet;
        public string ContentType => IsHtmlDocument ? "text/html" : "application/xml";
        internal bool IsHtmlDocument { get; set; }
        internal bool HtmlParserScriptingEnabled { get; set; }
        internal Document TemplateDocument => _templateDocument ??= new Document { IsHtmlDocument = IsHtmlDocument, HtmlParserScriptingEnabled = HtmlParserScriptingEnabled };
        public DocumentType? Doctype { get { foreach (var n in ChildNodes) if (n is DocumentType d) return d; return null; } }
        public Element? DocumentElement => FirstElementChild;
        public HTMLElement? Head => FindHtmlChild("head");
        public HTMLElement? Body
        {
            get => FindHtmlChild("body") ?? FindHtmlChild("frameset");
            set
            {
                if (value == null || value.LocalName is not ("body" or "frameset") || value.NamespaceURI != Names.Html)
                    throw DomException.HierarchyRequest("Body must be an HTML body or frameset.");
                if (Body is { } old) old.ParentNode!.ReplaceChild(value, old);
                else (DocumentElement ?? throw DomException.HierarchyRequest("Missing document element.")).AppendChild(value);
            }
        }
        private HTMLElement? FindHtmlChild(string name)
        {
            if (DocumentElement is not { NamespaceURI: Names.Html, LocalName: "html" } root) return null;
            foreach (var n in root.ChildNodes) if (n is HTMLElement e && e.LocalName == name) return e;
            return null;
        }
        public string Title
        {
            get => HtmlText.CollapseWhitespace(GetElementsByTagNameNS(Names.Html, "title").Item(0)?.TextContent ?? string.Empty);
            set
            {
                var title = GetElementsByTagNameNS(Names.Html, "title").Item(0);
                if (title == null && Head != null) { title = CreateElement("title"); Head.AppendChild(title); }
                if (title != null) title.TextContent = value;
            }
        }
        public Element? GetElementById(string id)
        { foreach (var n in InclusiveDescendants()) if (n is Element e && e.Id == id && id.Length > 0) return e; return null; }
        public Element CreateElement(string name, ElementCreationOptions? options = null)
        {
            Names.Validate(name);
            if (IsHtmlDocument) name = Names.AsciiLower(name);
            var element = IsHtmlDocument ? HtmlElementFactory.Create(this, name) : new Element(this, null, name);
            if (options?.Is != null) element.SetAttribute("is", options.Is);
            return element;
        }
        public Element CreateElementNS(string? ns, string name, ElementCreationOptions? options = null)
        {
            var parts = Names.Extract(ns, name);
            var element = parts.Namespace == Names.Html ? HtmlElementFactory.Create(this, parts.Local) : new Element(this, parts.Namespace, parts.Local);
            element.PrefixInternal = parts.Prefix;
            if (options?.Is != null) element.SetAttribute("is", options.Is);
            return element;
        }
        public DocumentFragment CreateDocumentFragment() => new(this);
        public Text CreateTextNode(string data) => new(this, data);
        public Comment CreateComment(string data) => new(this, data);
        public CDATASection CreateCDATASection(string data)
        {
            if (IsHtmlDocument) throw DomException.NotSupported("CDATA is not supported in HTML documents.");
            if (data.Contains("]]>", StringComparison.Ordinal)) throw DomException.InvalidCharacter("CDATA terminator in data.");
            return new CDATASection(this, data);
        }
        public ProcessingInstruction CreateProcessingInstruction(string target, string data)
        {
            Names.Validate(target);
            if (data.Contains("?>", StringComparison.Ordinal)) throw DomException.InvalidCharacter("PI terminator in data.");
            return new ProcessingInstruction(this, target, data);
        }
        public Attr CreateAttribute(string name)
        { Names.Validate(name); return new Attr(this, null, null, IsHtmlDocument ? Names.AsciiLower(name) : name, string.Empty); }
        public Attr CreateAttributeNS(string? ns, string name)
        { var p = Names.Extract(ns, name); return new Attr(this, p.Namespace, p.Prefix, p.Local, string.Empty); }
        public Event CreateEvent(string name)
        {
            Event e = Names.AsciiLower(name) switch
            {
                "event" or "events" or "htmlevents" => new Event(string.Empty),
                "customevent" => new CustomEvent(string.Empty),
                _ => throw DomException.NotSupported("Unsupported event interface: " + name)
            };
            e.MarkUninitialized(); return e;
        }
        public Node ImportNode(Node node, bool deep = false)
        { if (node is Document or ShadowRoot) throw DomException.NotSupported("Cannot import this node."); return node.CloneInto(this, deep); }
        public Node AdoptNode(Node node)
        {
            using var reactions = Reactions();
            using var oldReactions = node.NodeDocument.Reactions();
            if (node is Document) throw DomException.NotSupported("Cannot adopt a document.");
            if (node is ShadowRoot) throw DomException.HierarchyRequest("Cannot adopt a shadow root.");
            if (node is Attr { OwnerElement: { } owner } attr) owner.RemoveAttributeNode(attr);
            node.ParentNode?.RemoveChild(node); node.AdoptInto(this); return node;
        }
    }
    /// <summary>DOM ElementCreationOptions (customized built-in identity).</summary>
    public sealed class ElementCreationOptions { public string? Is { get; init; } }
}
