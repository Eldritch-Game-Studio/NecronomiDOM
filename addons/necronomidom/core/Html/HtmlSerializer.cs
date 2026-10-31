using System.Text;
using NecronomiDom.Dom;

namespace NecronomiDom.Html
{
    /// <summary>WHATWG HTML § 13.3 fragment serialization, including optional serializable shadow trees.</summary>
    public static class HtmlSerializer
    {
        public static string SerializeChildren(Node node, bool serializableShadowRoots = false)
        { var output = new StringBuilder(); Children(node, output, serializableShadowRoots); return output.ToString(); }
        public static string SerializeOuter(Node node, bool serializableShadowRoots = false)
        { var output = new StringBuilder(); Outer(node, output, serializableShadowRoots); return output.ToString(); }
        private static void Children(Node node, StringBuilder output, bool shadows)
        {
            if (node is Element { ShadowRootInternal: { Serializable: true } root } && shadows)
            {
                output.Append("<template shadowrootmode=\"").Append(root.Mode == ShadowRootMode.Open ? "open" : "closed").Append('"');
                if (root.DelegatesFocus) output.Append(" shadowrootdelegatesfocus=\"\"");
                if (root.Clonable) output.Append(" shadowrootclonable=\"\"");
                output.Append(" shadowrootserializable=\"\">");
                foreach (var child in root.ChildNodes) Outer(child, output, shadows);
                output.Append("</template>");
            }
            var parent = node is HTMLTemplateElement template ? template.Content : node;
            foreach (var child in parent.ChildNodes) Outer(child, output, shadows);
        }
        private static void Outer(Node node, StringBuilder output, bool shadows)
        {
            switch (node)
            {
                case Element e:
                    var name = e.NamespaceURI is Names.Html or "http://www.w3.org/2000/svg" or "http://www.w3.org/1998/Math/MathML" ? e.LocalName : e.QualifiedName;
                    output.Append('<').Append(name);
                    foreach (var a in e.Attributes)
                    {
                        var attributeName = a.NamespaceURI switch
                        {
                            Names.Xml => "xml:" + a.LocalName,
                            Names.Xmlns => a.LocalName == "xmlns" ? "xmlns" : "xmlns:" + a.LocalName,
                            "http://www.w3.org/1999/xlink" => "xlink:" + a.LocalName,
                            _ => a.Name
                        };
                        output.Append(' ').Append(attributeName).Append("=\""); Escape(a.Value, output, true); output.Append('"');
                    }
                    output.Append('>');
                    if (e.NamespaceURI == Names.Html && IsVoid(e.LocalName)) return;
                    if (e.NamespaceURI == Names.Html && e.LocalName is "pre" or "textarea" or "listing" && e.FirstChild is Text leading && leading.Data.StartsWith('\n'))
                        output.Append('\n');
                    Children(e, output, shadows); output.Append("</").Append(name).Append('>'); break;
                case Text text:
                    if (text.ParentElement is { NamespaceURI: Names.Html } parent && (IsRaw(parent.LocalName) || parent.LocalName == "noscript" && parent.NodeDocument.HtmlParserScriptingEnabled)) output.Append(text.Data);
                    else Escape(text.Data, output, false);
                    break;
                case Comment comment: output.Append("<!--").Append(comment.Data).Append("-->"); break;
                case DocumentType type: output.Append("<!DOCTYPE ").Append(type.Name).Append('>'); break;
                case ProcessingInstruction pi: output.Append("<?").Append(pi.Target).Append(' ').Append(pi.Data).Append('>'); break;
                case Document or DocumentFragment: Children(node, output, shadows); break;
            }
        }
        private static bool IsVoid(string name) => name is "area" or "base" or "basefont" or "bgsound" or "link" or "meta" or "input" or "embed" or "param" or "hr" or "img" or "br" or "wbr" or "source" or "track" or "col";
        private static bool IsRaw(string name) => name is "style" or "script" or "xmp" or "iframe" or "noembed" or "noframes" or "plaintext";
        private static void Escape(string text, StringBuilder output, bool attribute)
        {
            foreach (var c in text)
                switch (c)
                {
                    case '&': output.Append("&amp;"); break;
                    case '\u00A0': output.Append("&nbsp;"); break;
                    case '"' when attribute: output.Append("&quot;"); break;
                    case '<' when !attribute: output.Append("&lt;"); break;
                    case '>' when !attribute: output.Append("&gt;"); break;
                    default: output.Append(c); break;
                }
        }
    }
}
