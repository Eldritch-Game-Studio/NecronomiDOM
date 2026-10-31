using System;
using System.Collections.Generic;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Dom;
using NecronomiDom.Dom;
using NecronomiDom.Url;
using A = AngleSharp.Dom;

namespace NecronomiDom.Html
{
    /// <summary>NecronomiDOM markup-profile import resource limits; scripting affects parsing only, never execution.</summary>
    public sealed class HtmlImportOptions
    {
        /// <summary>WHATWG scripting flag for tokenization only; product defaults off and never executes JavaScript.</summary>
        public bool IsScripting { get; init; }
        public int MaxInputCharacters { get; init; } = 4 * 1024 * 1024;
        public int MaxNodes { get; init; } = 100000;
        public int MaxTextCharacters { get; init; } = 8 * 1024 * 1024;
        public int MaxTreeDepth { get; init; } = 1024;
    }
    /// <summary>WHATWG HTML § 13 import result, including positioned parse warnings.</summary>
    public sealed record HtmlImportResult(Document Document, IReadOnlyList<WebDiagnostic> Diagnostics);
    /// <summary>WHATWG HTML § 13 fragment-import result for parser conformance tooling.</summary>
    public sealed record HtmlFragmentImportResult(DocumentFragment Fragment, IReadOnlyList<WebDiagnostic> Diagnostics);
    /// <summary>AngleSharp WHATWG tree construction with product scripting disabled, no JS or resource loaders.</summary>
    public static class HtmlImporter
    {
        public static HtmlImportResult Import(string html, string sourcePath, WebUrl documentUrl, HtmlImportOptions? options = null)
        {
            options ??= new HtmlImportOptions(); CheckInput(html, options);
            var positions = new HtmlSourceMap(html);
            var diagnostics = new List<WebDiagnostic>(); var parser = Parser(sourcePath, diagnostics, positions, options);
            var parsed = parser.ParseDocument(html);
            var document = new Document { IsHtmlDocument = true, HtmlParserScriptingEnabled = options.IsScripting, BaseUrl = documentUrl, CompatMode = parsed.CompatMode };
            var converter = new Converter(options, positions);
            foreach (var node in parsed.ChildNodes) converter.Convert(node, document, document, 0);
            return new HtmlImportResult(document, diagnostics);
        }
        public static HtmlFragmentImportResult ImportFragment(string html, Element context, string sourcePath, HtmlImportOptions? options = null)
        {
            options ??= new HtmlImportOptions(); CheckInput(html, options);
            var positions = new HtmlSourceMap(html);
            var diagnostics = new List<WebDiagnostic>(); var parser = Parser(sourcePath, diagnostics, positions, options);
            var contextDocument = parser.ParseDocument((context.NodeDocument.CompatMode == "BackCompat" ? "" : "<!doctype html>") + "<html><head></head><body></body></html>");
            var angleContext = contextDocument.CreateElement(context.NamespaceURI!, context.QualifiedName);
            foreach (var attr in context.Attributes) angleContext.SetAttribute(attr.NamespaceURI, attr.Name, attr.Value);
            if (context.NamespaceURI != Names.Html || context.LocalName != "form")
                for (var ancestor = context.ParentNode; ancestor != null; ancestor = ancestor.ParentNode)
                    if (ancestor is Element { NamespaceURI: Names.Html, LocalName: "form" })
                    {
                        contextDocument.CreateElement("form").AppendChild(angleContext);
                        break;
                    }
            diagnostics.Clear();
            var nodes = parser.ParseFragment(html, angleContext);
            var fragment = context.NodeDocument.CreateDocumentFragment(); var converter = new Converter(options, positions, false);
            foreach (var node in nodes) converter.Convert(node, fragment, context.NodeDocument, 0);
            return new HtmlFragmentImportResult(fragment, diagnostics);
        }
        private static void CheckInput(string html, HtmlImportOptions options)
        {
            if (options.MaxInputCharacters < 0 || options.MaxNodes < 0 || options.MaxTextCharacters < 0 || options.MaxTreeDepth < 0) throw new ArgumentOutOfRangeException(nameof(options));
            if (html.Length > options.MaxInputCharacters) throw new FormatException("HTML input character limit exceeded.");
        }
        private static HtmlParser Parser(string path, List<WebDiagnostic> diagnostics, HtmlSourceMap positions, HtmlImportOptions options)
        {
            var parser = new HtmlParser(new HtmlParserOptions { IsScripting = options.IsScripting, IsSupportingProcessingInstructions = true, IsKeepingSourceReferences = true });
            parser.Error += (_, error) =>
            {
                var e = (AngleSharp.Html.Dom.Events.HtmlErrorEvent)error;
                var position = positions.At(e.Position.Index);
                diagnostics.Add(new WebDiagnostic(WebDiagnosticSeverity.Warning, path, position.Line, position.Column, "HTML" + e.Code, e.Message));
            };
            return parser;
        }
        private sealed class Converter
        {
            private readonly HtmlImportOptions _options;
            private readonly bool _declarative;
            private readonly HtmlSourceMap _positions;
            private int _nodes;
            private int _text;
            internal Converter(HtmlImportOptions options, HtmlSourceMap positions, bool declarative = true) { _options = options; _positions = positions; _declarative = declarative; }
            internal void Convert(A.INode source, Node parent, Document document, int depth)
            {
                if (++_nodes > _options.MaxNodes || depth > _options.MaxTreeDepth) throw new FormatException("HTML tree resource limit exceeded.");
                Node target;
                switch (source)
                {
                    case A.IDocumentType d: target = new DocumentType(document, d.Name, d.PublicIdentifier, d.SystemIdentifier); break;
                    case A.IText text: target = document.CreateTextNode(Text(text.Data)); break;
                    case A.IComment comment: target = document.CreateComment(Text(comment.Data)); break;
                    case A.IProcessingInstruction instruction: target = new ProcessingInstruction(document, instruction.Target, Text(instruction.Data)); break;
                    case A.IElement element:
                        if (_declarative && element is IHtmlTemplateElement template && parent is Element host && host.NamespaceURI == Names.Html && Element.CanHostShadow(host.LocalName) && (Names.AsciiEqual(element.GetAttribute("shadowrootmode"), "open") || Names.AsciiEqual(element.GetAttribute("shadowrootmode"), "closed")) && host.ShadowRootInternal == null)
                        {
                            var shadow = host.AttachShadow(new ShadowRootInit { Mode = Names.AsciiEqual(element.GetAttribute("shadowrootmode"), "open") ? ShadowRootMode.Open : ShadowRootMode.Closed, DelegatesFocus = element.HasAttribute("shadowrootdelegatesfocus"), Clonable = element.HasAttribute("shadowrootclonable"), Serializable = element.HasAttribute("shadowrootserializable") });
                            shadow.Declarative = true;
                            foreach (var child in template.Content.ChildNodes) Convert(child, shadow, document, depth + 1);
                            return;
                        }
                        var e = element.NamespaceUri == Names.Html ? HtmlElementFactory.Create(document, element.LocalName) : new Element(document, element.NamespaceUri, element.LocalName);
                        e.PrefixInternal = element.Prefix;
                        if (element.SourceReference is { } reference)
                            e.SourceLocation = reference.Position.Index < 0 ? null : _positions.At(reference.Position.Index);
                        foreach (var attr in element.Attributes)
                            e.SetAttributeNodeNS(new Attr(document, string.IsNullOrEmpty(attr.NamespaceUri) ? null : attr.NamespaceUri, attr.Prefix, attr.LocalName, Text(attr.Value)));
                        parent.AppendChild(e);
                        if (element is IHtmlTemplateElement t && e is HTMLTemplateElement ours)
                        { foreach (var child in t.Content.ChildNodes) Convert(child, ours.Content, ours.Content.NodeDocument, depth + 1); }
                        else foreach (var child in element.ChildNodes) Convert(child, e, document, depth + 1);
                        return;
                    default: throw new FormatException("Unsupported HTML parser node: " + source.NodeType);
                }
                parent.AppendChild(target);
            }
            private string Text(string data)
            { if (data.Length > _options.MaxTextCharacters - _text) throw new FormatException("HTML expanded text limit exceeded."); _text += data.Length; return data; }
        }
    }
}
