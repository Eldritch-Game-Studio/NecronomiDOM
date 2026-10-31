using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    public sealed partial class Window
    {
        /// <summary>HTML named access: document-tree HTML ids and embed/form/img/object names. Embedded navigable providers are outside this no-navigation document profile.</summary>
        public object? this[string name]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(name);
                if (name.Length == 0) return null;
                Element? first = null;
                for (Node? node = Document.FirstChild; node != null; node = node.NextInTreeOrder(Document))
                {
                    if (node is not Element element || !MatchesNamedProperty(element, name)) continue;
                    if (first != null) return new HTMLCollection(Document, candidate => MatchesNamedProperty(candidate, name));
                    first = element;
                }
                return first;
            }
        }
        internal IReadOnlyList<string> NamedPropertyNames
        {
            get
            {
                var names = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (Node? node = Document.FirstChild; node != null; node = node.NextInTreeOrder(Document))
                {
                    if (node is not Element { NamespaceURI: Names.Html } element) continue;
                    if (element.Id.Length > 0 && seen.Add(element.Id)) names.Add(element.Id);
                    if (HasNamedAttribute(element) && element.GetAttribute("name") is { Length: > 0 } name && seen.Add(name)) names.Add(name);
                }
                return names;
            }
        }
        private static bool MatchesNamedProperty(Element element, string name) => element.NamespaceURI == Names.Html &&
            (element.Id == name || HasNamedAttribute(element) && element.GetAttribute("name") == name);
        private static bool HasNamedAttribute(Element element) => element.LocalName is "embed" or "form" or "img" or "object";
    }
}
