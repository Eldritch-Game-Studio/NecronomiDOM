using System;

namespace NecronomiDom.Dom
{
    public abstract partial class Node
    {
        public Node CloneNode(bool deep = false)
        { if (this is ShadowRoot) throw DomException.NotSupported("Shadow roots cannot be cloned directly."); return CloneInto(NodeDocument, deep); }
        internal Node CloneInto(Document document, bool deep)
        {
            Node copy = this switch
            {
                Document d => new Document { IsHtmlDocument = d.IsHtmlDocument, HtmlParserScriptingEnabled = d.HtmlParserScriptingEnabled, BaseUrl = d.BaseUrl, CompatMode = d.CompatMode },
                Element e => CloneElement(e, document),
                Attr a => new Attr(document, a.NamespaceURI, a.Prefix, a.LocalName, a.Value),
                CDATASection c => new CDATASection(document, c.Data),
                Text t => new Text(document, t.Data),
                Comment c => new Comment(document, c.Data),
                ProcessingInstruction p => new ProcessingInstruction(document, p.Target, p.Data),
                DocumentType d => new DocumentType(document, d.Name, d.PublicId, d.SystemId),
                DocumentFragment => new DocumentFragment(document),
                _ => throw DomException.NotSupported("Unsupported node kind.")
            };
            if (copy is Document newDocument) document = newDocument;
            if (this is Element { ShadowRootInternal: { Clonable: true } root } && copy is Element host)
            {
                var shadow = host.AttachShadow(new ShadowRootInit { Mode = root.Mode, DelegatesFocus = root.DelegatesFocus, SlotAssignment = root.SlotAssignment, Clonable = root.Clonable, Serializable = root.Serializable });
                foreach (var child in root.ChildNodes) shadow.AppendChild(child.CloneInto(document, true));
            }
            if (deep)
            {
                foreach (var child in ChildNodes) copy.AppendChild(child.CloneInto(document, true));
                if (this is HTMLTemplateElement template && copy is HTMLTemplateElement newTemplate)
                    foreach (var child in template.Content.ChildNodes) newTemplate.Content.AppendChild(child.CloneInto(newTemplate.Content.NodeDocument, true));
            }
            return copy;
        }
        private static Element CloneElement(Element e, Document document)
        {
            var copy = e.NamespaceURI == Names.Html ? HtmlElementFactory.Create(document, e.LocalName) : new Element(document, e.NamespaceURI, e.LocalName);
            copy.PrefixInternal = e.Prefix;
            foreach (var a in e.AttributeList) copy.SetAttributeNodeNS(new Attr(document, a.NamespaceURI, a.Prefix, a.LocalName, a.Value));
            if (e is HTMLInputElement input && copy is HTMLInputElement newInput) { newInput.Value = input.Value; newInput.Checked = input.Checked; }
            if (e is HTMLTextAreaElement area && copy is HTMLTextAreaElement newArea) newArea.Value = area.Value;
            if (e is HTMLOptionElement option && copy is HTMLOptionElement newOption) newOption.Selected = option.Selected;
            return copy;
        }
        public bool IsEqualNode(Node? other)
        {
            if (other == null || NodeType != other.NodeType || ChildCount != other.ChildCount) return false;
            if (this is Element e)
            {
                if (other is not Element o || e.NamespaceURI != o.NamespaceURI || e.Prefix != o.Prefix || e.LocalName != o.LocalName || e.AttributeList.Count != o.AttributeList.Count) return false;
                foreach (var a in e.AttributeList) if (o.GetAttributeNodeNS(a.NamespaceURI, a.LocalName)?.Value != a.Value) return false;
            }
            else if (this is DocumentType d && other is DocumentType od && (d.Name != od.Name || d.PublicId != od.PublicId || d.SystemId != od.SystemId)) return false;
            else if (this is Attr a && other is Attr oa && (a.NamespaceURI != oa.NamespaceURI || a.LocalName != oa.LocalName || a.Value != oa.Value)) return false;
            else if (this is CharacterData c && other is CharacterData oc && (c.Data != oc.Data || NodeName != other.NodeName)) return false;
            var left = FirstChild; var right = other.FirstChild;
            while (left != null) { if (!left.IsEqualNode(right)) return false; left = left.NextSibling; right = right!.NextSibling; }
            return true;
        }
    }
}
