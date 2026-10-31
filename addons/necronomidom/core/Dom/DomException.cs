using System;

namespace NecronomiDom.Dom
{
    /// <summary>Web IDL DOMException names and legacy codes (DOM § 4).</summary>
    public sealed class DomException : Exception
    {
        public DomException(string message = "", string name = "Error") : base(message) { Name = name; }
        public string Name { get; }
        public ushort Code => Name switch
        {
            "IndexSizeError" => 1, "HierarchyRequestError" => 3, "WrongDocumentError" => 4,
            "InvalidCharacterError" => 5, "NoModificationAllowedError" => 7, "NotFoundError" => 8,
            "NotSupportedError" => 9, "InUseAttributeError" => 10, "InvalidStateError" => 11,
            "SyntaxError" => 12, "InvalidModificationError" => 13, "NamespaceError" => 14,
            "InvalidAccessError" => 15, "TypeMismatchError" => 17, "SecurityError" => 18,
            "NetworkError" => 19, "AbortError" => 20, "URLMismatchError" => 21,
            "QuotaExceededError" => 22, "TimeoutError" => 23, "InvalidNodeTypeError" => 24,
            "DataCloneError" => 25, _ => 0
        };
        public static DomException IndexSize(string m) => new(m, "IndexSizeError");
        public static DomException HierarchyRequest(string m) => new(m, "HierarchyRequestError");
        public static DomException InvalidCharacter(string m) => new(m, "InvalidCharacterError");
        public static DomException Namespace(string m) => new(m, "NamespaceError");
        public static DomException NotFound(string m) => new(m, "NotFoundError");
        public static DomException NotSupported(string m) => new(m, "NotSupportedError");
        public static DomException InvalidState(string m) => new(m, "InvalidStateError");
        public static DomException Syntax(string m) => new(m, "SyntaxError");
        public static DomException InvalidModification(string m) => new(m, "InvalidModificationError");
        public static DomException InUseAttribute(string m) => new(m, "InUseAttributeError");
    }
    /// <summary>Web IDL TypeError-equivalent for invalid DOM arguments.</summary>
    public sealed class DomTypeError : Exception
    {
        public DomTypeError(string message) : base(message) { }
    }
    internal static partial class Names
    {
        internal const string Html = "http://www.w3.org/1999/xhtml";
        internal const string Xml = "http://www.w3.org/XML/1998/namespace";
        internal const string Xmlns = "http://www.w3.org/2000/xmlns/";
        internal static void Validate(string name)
        {
            if (!IsXmlName(name)) throw DomException.InvalidCharacter("Invalid XML name: " + name);
        }
        internal static (string? Namespace, string? Prefix, string Local) Extract(string? ns, string name)
        {
            ns = string.IsNullOrEmpty(ns) ? null : ns;
            Validate(name);
            var colon = name.IndexOf(':');
            var prefix = colon < 0 ? null : name.Substring(0, colon);
            var local = colon < 0 ? name : name.Substring(colon + 1);
            if (!IsXmlName(local, false) || prefix != null && !IsXmlName(prefix, false))
                throw DomException.Namespace("Invalid qualified name: " + name);
            if (prefix != null && ns == null || prefix == "xml" && ns != Xml ||
                (name == "xmlns" || prefix == "xmlns") && ns != Xmlns ||
                ns == Xmlns && name != "xmlns" && prefix != "xmlns")
                throw DomException.Namespace("Namespace does not match the qualified name.");
            return (ns, prefix, local);
        }
    }
    /// <summary>DOM mutation integration hook for downstream style invalidation.</summary>
    public interface IDomChangeSink
    {
        /// <summary>Whether attribute notifications require actual serialized old values; consumers opt out explicitly.</summary>
        bool RequiresAttributeOldValue => true;
        void ChildrenChanged(Node parent);
        void AttributeChanged(Element element, string localName, string? oldValue);
        void CharacterDataChanged(CharacterData node);
        void NodeInserted(Node node);
        void NodeRemoved(Node node, Node oldParent);
    }
}
