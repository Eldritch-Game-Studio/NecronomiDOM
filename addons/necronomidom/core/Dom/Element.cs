using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.9 Element, namespace-aware attributes and shadow attachment.</summary>
    public partial class Element : Node
    {
        private NamedNodeMap? _attributes;
        private DOMTokenList? _classList;
        internal readonly List<Attr> AttributeList = new();
        internal Element(Document doc, string? ns, string local) : base(doc) { NamespaceURI = ns; LocalName = local; }
        public string? NamespaceURI { get; }
        private string? _prefix;
        private string? _qualifiedName;
        private string? _htmlTagName;
        internal string? PrefixInternal { get => _prefix; set { _prefix = value; _qualifiedName = null; _htmlTagName = null; } }
        public string? Prefix => PrefixInternal;
        public string LocalName { get; }
        public string TagName => NamespaceURI == Names.Html && NodeDocument.IsHtmlDocument ? _htmlTagName ??= Names.AsciiUpper(QualifiedName) : QualifiedName;
        internal string QualifiedName => _qualifiedName ??= Prefix == null ? LocalName : Prefix + ":" + LocalName;
        public override ushort NodeType => ElementNode;
        public override string NodeName => TagName;
        public string Id { get => GetAttribute("id") ?? ""; set => SetAttribute("id", value); }
        public string ClassName { get => GetAttribute("class") ?? ""; set => SetAttribute("class", value); }
        public string Slot { get => GetAttribute("slot") ?? ""; set => SetAttribute("slot", value); }
        public DOMTokenList ClassList => _classList ??= new DOMTokenList(this, "class");
        public NamedNodeMap Attributes => _attributes ??= new NamedNodeMap(this);
        public bool HasAttributes() => AttributeList.Count > 0;
        public IReadOnlyList<string> GetAttributeNames()
        { var names = new string[AttributeList.Count]; for (var i = 0; i < names.Length; i++) names[i] = AttributeList[i].Name; return names; }
        private string Fold(string name) => NamespaceURI == Names.Html && NodeDocument.IsHtmlDocument ? Names.AsciiLower(name) : name;
        public Attr? GetAttributeNode(string name)
        { name = Fold(name); foreach (var a in AttributeList) if (a.Name == name) return a; return null; }
        public Attr? GetAttributeNodeNS(string? ns, string local)
        { ns = string.IsNullOrEmpty(ns) ? null : ns; foreach (var a in AttributeList) if (a.NamespaceURI == ns && a.LocalName == local) return a; return null; }
        public string? GetAttribute(string name) => GetAttributeNode(name)?.Value;
        public string? GetAttributeNS(string? ns, string local) => GetAttributeNodeNS(ns, local)?.Value;
        public bool HasAttribute(string name) => GetAttributeNode(name) != null;
        public bool HasAttributeNS(string? ns, string local) => GetAttributeNodeNS(ns, local) != null;
        public void SetAttribute(string name, string value)
        {
            Names.Validate(name); name = Fold(name);
            if (GetAttributeNode(name) is { } a) a.Value = value;
            else SetAttributeNode(new Attr(NodeDocument, null, null, name, value));
        }
        public void SetAttributeNS(string? ns, string name, string value)
        {
            var p = Names.Extract(ns, name);
            if (GetAttributeNodeNS(p.Namespace, p.Local) is { } a) { a.Prefix = p.Prefix; a.Value = value; }
            else SetAttributeNodeNS(new Attr(NodeDocument, p.Namespace, p.Prefix, p.Local, value));
        }
        public void RemoveAttribute(string name) { if (GetAttributeNode(name) is { } a) RemoveAttributeNode(a); }
        public void RemoveAttributeNS(string? ns, string local) { if (GetAttributeNodeNS(ns, local) is { } a) RemoveAttributeNode(a); }
        public bool ToggleAttribute(string name, bool? force = null)
        {
            Names.Validate(name);
            if (!HasAttribute(name)) { if (force != false) { SetAttribute(name, ""); return true; } return false; }
            if (force != true) { RemoveAttribute(name); return false; } return true;
        }
        public Attr? SetAttributeNode(Attr attr) => SetAttributeNodeNS(attr);
        public Attr? SetAttributeNodeNS(Attr attr)
        {
            if (attr.OwnerElement != null && !ReferenceEquals(attr.OwnerElement, this)) throw DomException.InUseAttribute("Attribute already belongs to an element.");
            var old = GetAttributeNodeNS(attr.NamespaceURI, attr.LocalName);
            if (ReferenceEquals(old, attr)) return old;
            if (old == null) AttributeList.Add(attr);
            else { old.FreezeValueProvider(); var index = AttributeList.IndexOf(old); AttributeList[index] = attr; old.OwnerElement = null; }
            attr.NodeDocument = NodeDocument; attr.OwnerElement = this; NotifyAttribute(attr, old?.Value, attr.Value); return old;
        }
        public Attr RemoveAttributeNode(Attr attr)
        {
            if (!ReferenceEquals(attr.OwnerElement, this)) throw DomException.NotFound("Attribute is not owned by this element.");
            attr.FreezeValueProvider(); AttributeList.Remove(attr); attr.OwnerElement = null; NotifyAttribute(attr, attr.Value, null); return attr;
        }
        internal void NotifyAttribute(Attr attr, string? oldValue, string? newValue)
        {
            using var reactions = NodeDocument.Reactions();
            MutationObserver.Queue(this, "attributes", attributeName: attr.LocalName, attributeNamespace: attr.NamespaceURI, oldValue: oldValue);
            AttributeChanged(attr.LocalName, attr.NamespaceURI, oldValue, newValue);
            NodeDocument.ChangeSink?.AttributeChanged(this, attr.LocalName, oldValue);
            CustomDefinition?.Attribute(this, attr.LocalName, oldValue, newValue, attr.NamespaceURI);
            if (attr.NamespaceURI == null && attr.LocalName is "slot" or "name") SlotDistribution.Refresh(this);
            if (attr.NamespaceURI == null && attr.LocalName is "disabled" or "inert" or "hidden") NodeDocument.FixupFocus();
        }
        protected internal virtual void AttributeChanged(string localName, string? ns, string? oldValue, string? newValue) => SelectedContentProjection.Refresh(this);
        internal ShadowRoot? ShadowRootInternal { get; private set; }
        public ShadowRoot? ShadowRoot => ShadowRootInternal?.Mode == ShadowRootMode.Open ? ShadowRootInternal : null;
        public HTMLSlotElement? AssignedSlot => AssignedSlotInternal?.Root is ShadowRoot { Mode: ShadowRootMode.Closed } ? null : AssignedSlotInternal;
        internal CustomElementDefinition? CustomDefinition { get; set; }
        internal bool CustomFailed { get; set; }
        internal bool CustomConstructing { get; set; }
        internal void NotifyStateChanged() { NodeDocument.ChangeSink?.AttributeChanged(this, ":state", null); SelectedContentProjection.Refresh(this); }
        public bool IsDefined => !CustomElementRegistry.IsCustomName(LocalName) || CustomDefinition != null && !CustomConstructing && !CustomFailed;
        public ShadowRoot AttachShadow(ShadowRootInit init)
        {
            if (NamespaceURI != Names.Html || !CanHostShadow(LocalName)) throw DomException.NotSupported("Element cannot host a shadow root.");
            if (ShadowRootInternal is { } existing)
            {
                if (!existing.Declarative || existing.Mode != init.Mode) throw DomException.NotSupported("Element already has a shadow root.");
                existing.ReplaceChildren(); existing.Declarative = false; return existing;
            }
            ShadowRootInternal = new ShadowRoot(this, init); SlotDistribution.Refresh(this);
            NodeDocument.ChangeSink?.ChildrenChanged(this); return ShadowRootInternal;
        }
        internal static bool CanHostShadow(string name) => CustomElementRegistry.IsCustomName(name) || name is "article" or "aside" or "blockquote" or "body" or "div" or "footer" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "header" or "main" or "nav" or "p" or "section" or "span";
        public Element? InsertAdjacentElement(string position, Element element)
        {
            switch (Names.AsciiLower(position))
            {
                case "beforebegin": if (ParentElement == null) return null; ParentElement.InsertBefore(element, this); break;
                case "afterend": if (ParentElement == null) return null; ParentElement.InsertBefore(element, NextSibling); break;
                case "afterbegin": InsertBefore(element, FirstChild); break;
                case "beforeend": AppendChild(element); break;
                default: throw DomException.Syntax("Invalid adjacent position.");
            }
            return element;
        }
        public void InsertAdjacentText(string position, string data)
        {
            var text = NodeDocument.CreateTextNode(data);
            switch (Names.AsciiLower(position))
            {
                case "beforebegin": ParentNode?.InsertBefore(text, this); break;
                case "afterend": ParentNode?.InsertBefore(text, NextSibling); break;
                case "afterbegin": InsertBefore(text, FirstChild); break;
                case "beforeend": AppendChild(text); break;
                default: throw DomException.Syntax("Invalid adjacent position.");
            }
        }
    }
}
