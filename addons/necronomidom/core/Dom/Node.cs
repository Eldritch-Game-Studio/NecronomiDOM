using System.Collections.Generic;
using System.Threading;

namespace NecronomiDom.Dom
{
    /// <summary>
    /// DOM § 4.4 <c>Node</c>: node tree links (DOM § 1.1 trees), node document, registered observer list and the
    /// read-only tree accessors. Mutation algorithms are in <c>Node.Mutation.cs</c>, cloning/equality in
    /// <c>Node.Clone.cs</c>, text/compare/lookup in <c>Node.Tree.cs</c>.
    /// </summary>
    public abstract partial class Node : EventTarget
    {
        public const ushort ElementNode = 1;
        public const ushort AttributeNode = 2;
        public const ushort TextNode = 3;
        public const ushort CDATASectionNode = 4;
        public const ushort EntityReferenceNode = 5;
        public const ushort EntityNode = 6;
        public const ushort ProcessingInstructionNode = 7;
        public const ushort CommentNode = 8;
        public const ushort DocumentNode = 9;
        public const ushort DocumentTypeNode = 10;
        public const ushort DocumentFragmentNode = 11;
        public const ushort NotationNode = 12;

        public const ushort DocumentPositionDisconnected = 0x01;
        public const ushort DocumentPositionPreceding = 0x02;
        public const ushort DocumentPositionFollowing = 0x04;
        public const ushort DocumentPositionContains = 0x08;
        public const ushort DocumentPositionContainedBy = 0x10;
        public const ushort DocumentPositionImplementationSpecific = 0x20;

        private Document? _nodeDocument;
        private ChildNodeList? _childNodes;
        private static long _nextIdentity;
        private readonly long _identity = Interlocked.Increment(ref _nextIdentity);

        /// <summary>Creates a node whose node document is <paramref name="document"/> (null only for documents themselves).</summary>
        private protected Node(Document? document) => _nodeDocument = document;

        internal Node? ParentInternal { get; set; }

        internal Node? FirstChildInternal { get; set; }

        internal Node? LastChildInternal { get; set; }

        internal Node? PreviousSiblingInternal { get; set; }

        internal Node? NextSiblingInternal { get; set; }

        internal int ChildCount { get; set; }

        /// <summary>The registered observer list (DOM § 4.3), allocated on first observe.</summary>
        internal List<RegisteredObserver>? RegisteredObservers { get; set; }

        /// <summary>A slottable's assigned slot (DOM § 4.2.2.2).</summary>
        internal HTMLSlotElement? AssignedSlotInternal { get; set; }

        /// <summary>A slottable's manual slot assignment (DOM § 4.2.2.2).</summary>
        internal HTMLSlotElement? ManualSlotAssignment { get; set; }

        /// <summary>The node document; for a document, the document itself.</summary>
        internal Document NodeDocument
        {
            get => _nodeDocument ?? (Document)this;
            set => _nodeDocument = value;
        }

        public abstract ushort NodeType { get; }

        public abstract string NodeName { get; }

        /// <summary>The node document's document base URL, serialized (DOM § 4.4 baseURI).</summary>
        public string BaseURI => NodeDocument.DocumentBaseUrlHref;

        /// <summary>True when the shadow-including root is a document.</summary>
        public bool IsConnected => ShadowIncludingRoot is Document;

        /// <summary>The node document, or null for documents.</summary>
        public Document? OwnerDocument => this is Document ? null : NodeDocument;

        public Node? ParentNode => ParentInternal;

        public Element? ParentElement => ParentInternal as Element;

        public Node? FirstChild => FirstChildInternal;

        public Node? LastChild => LastChildInternal;

        public Node? PreviousSibling => PreviousSiblingInternal;

        public Node? NextSibling => NextSiblingInternal;

        /// <summary>The live child list.</summary>
        public NodeList ChildNodes => _childNodes ??= new ChildNodeList(this);

        public bool HasChildNodes() => FirstChildInternal != null;

        /// <summary>getRootNode(options): the root, or the shadow-including root when composed.</summary>
        public Node GetRootNode(GetRootNodeOptions? options = null) => options?.Composed == true ? ShadowIncludingRoot : Root;

        public bool IsSameNode(Node? other) => ReferenceEquals(this, other);

        /// <summary>contains(other): other is an inclusive descendant.</summary>
        public bool Contains(Node? other) => other != null && IsInclusiveAncestorOf(other);

        /// <summary>The root (DOM § 1.1).</summary>
        internal Node Root
        {
            get
            {
                var n = this;
                while (n.ParentInternal != null) n = n.ParentInternal;
                return n;
            }
        }

        /// <summary>The shadow-including root (DOM § 4.2.2).</summary>
        internal Node ShadowIncludingRoot
        {
            get
            {
                var root = Root;
                while (root is ShadowRoot sr) root = sr.Host.Root;
                return root;
            }
        }

        /// <summary>The index (number of preceding siblings).</summary>
        internal int Index
        {
            get
            {
                var i = 0;
                for (var n = PreviousSiblingInternal; n != null; n = n.PreviousSiblingInternal) i++;
                return i;
            }
        }

        internal bool IsInclusiveAncestorOf(Node other)
        {
            for (Node? n = other; n != null; n = n.ParentInternal)
                if (ReferenceEquals(n, this)) return true;
            return false;
        }

        /// <summary>Shadow-including inclusive ancestor: ancestors continue from a shadow root to its host.</summary>
        internal bool IsShadowIncludingInclusiveAncestorOf(Node other)
        {
            for (Node? n = other; n != null; n = n is ShadowRoot sr ? sr.Host : n.ParentInternal)
                if (ReferenceEquals(n, this)) return true;
            return false;
        }

        /// <summary>Host-including inclusive ancestor: ancestors continue from a fragment root to its host (template, shadow).</summary>
        internal bool IsHostIncludingInclusiveAncestorOf(Node other)
        {
            for (Node? n = other; n != null;)
            {
                if (ReferenceEquals(n, this)) return true;
                n = n.ParentInternal ?? (n as DocumentFragment)?.HostInternal;
            }
            return false;
        }

        /// <summary>Next node in tree order within <paramref name="root"/>'s subtree (null at the end).</summary>
        internal Node? NextInTreeOrder(Node root) => FirstChildInternal ?? NextSkippingChildren(root);

        /// <summary>Next node in tree order that is not a descendant of this, within <paramref name="root"/>.</summary>
        internal Node? NextSkippingChildren(Node root)
        {
            for (var n = this; n != null && !ReferenceEquals(n, root); n = n.ParentInternal)
                if (n.NextSiblingInternal != null) return n.NextSiblingInternal;
            return null;
        }

        /// <summary>Inclusive descendants in shadow-including tree order (element, its shadow root's subtree, then its children).</summary>
        internal IEnumerable<Node> ShadowIncludingInclusiveDescendants()
        {
            for (Node? node = this; node != null; node = node.NextInShadowIncludingOrder(this)) yield return node;
        }
        private Node? NextInShadowIncludingOrder(Node root)
        {
            if (this is Element { ShadowRootInternal: { } shadow }) return shadow;
            if (FirstChildInternal != null) return FirstChildInternal;
            for (Node? node = this; node != null && !ReferenceEquals(node, root);)
            {
                if (node is ShadowRoot sr)
                {
                    if (sr.Host.FirstChildInternal != null) return sr.Host.FirstChildInternal;
                    node = sr.Host;
                }
                else
                {
                    if (node.NextSiblingInternal != null) return node.NextSiblingInternal;
                    node = node.ParentInternal;
                }
            }
            return null;
        }

        /// <summary>Inclusive descendants in tree order (no shadow trees).</summary>
        internal IEnumerable<Node> InclusiveDescendants()
        {
            for (Node? n = this; n != null; n = n.NextInTreeOrder(this)) yield return n;
        }
    }

    /// <summary><c>GetRootNodeOptions</c> (DOM § 4.4).</summary>
    public sealed class GetRootNodeOptions
    {
        public bool Composed { get; init; }
    }
}
