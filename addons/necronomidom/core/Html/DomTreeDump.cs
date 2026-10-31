using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Dom;

namespace NecronomiDom.Html
{
    /// <summary>html5lib tree-construction test serialization, sorted attributes and template content markers.</summary>
    public static class DomTreeDump
    {
        public static string ToHtml5libFormat(Node node)
        {
            var output = new StringBuilder();
            if (node is Document or DocumentFragment) foreach (var child in node.ChildNodes) Dump(child, 0, output);
            else Dump(node, 0, output);
            return output.ToString().TrimEnd('\n');
        }
        private static void Line(StringBuilder output, int depth, string value)
        { output.Append("| ").Append(' ', depth * 2).Append(value).Append('\n'); }
        private static string Namespace(string? ns) => ns switch
        { "http://www.w3.org/2000/svg" => "svg ", "http://www.w3.org/1998/Math/MathML" => "math ", Names.Xml => "xml ", Names.Xmlns => "xmlns ", "http://www.w3.org/1999/xlink" => "xlink ", _ => "" };
        private static void Dump(Node node, int depth, StringBuilder output)
        {
            switch (node)
            {
                case Element e:
                    Line(output, depth, "<" + Namespace(e.NamespaceURI) + e.LocalName + ">");
                    var attrs = new List<Attr>(e.Attributes);
                    attrs.Sort((a, b) => StringComparer.Ordinal.Compare(Namespace(a.NamespaceURI) + a.LocalName, Namespace(b.NamespaceURI) + b.LocalName));
                    foreach (var a in attrs) Line(output, depth + 1, Namespace(a.NamespaceURI) + a.LocalName + "=\"" + a.Value + "\"");
                    foreach (var child in e.ChildNodes) Dump(child, depth + 1, output);
                    if (e is HTMLTemplateElement template)
                    { Line(output, depth + 1, "content"); foreach (var child in template.Content.ChildNodes) Dump(child, depth + 2, output); }
                    if (e.ShadowRootInternal is { } shadow)
                    { Line(output, depth + 1, shadow.Mode == ShadowRootMode.Open ? "shadow-root (open)" : "shadow-root (closed)"); foreach (var child in shadow.ChildNodes) Dump(child, depth + 2, output); }
                    break;
                case Text t: Line(output, depth, "\"" + t.Data + "\""); break;
                case Comment c: Line(output, depth, "<!-- " + c.Data + " -->"); break;
                case DocumentType d:
                    Line(output, depth, "<!DOCTYPE " + d.Name + (d.PublicId.Length > 0 || d.SystemId.Length > 0 ? " \"" + d.PublicId + "\" \"" + d.SystemId + "\"" : "") + ">"); break;
                case ProcessingInstruction p: Line(output, depth, "<?" + p.Target + " " + p.Data + ">"); break;
            }
        }
    }
}
