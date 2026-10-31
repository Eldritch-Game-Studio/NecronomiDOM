using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.3.1 MutationObserverInit with presence-sensitive old-value options.</summary>
    public sealed class MutationObserverInit
    {
        public bool ChildList { get; init; }
        public bool? Attributes { get; init; }
        public bool? CharacterData { get; init; }
        public bool Subtree { get; init; }
        public bool? AttributeOldValue { get; init; }
        public bool? CharacterDataOldValue { get; init; }
        public IReadOnlyList<string>? AttributeFilter { get; init; }
        internal bool ObserveAttributes => Attributes ?? (AttributeOldValue != null || AttributeFilter != null);
        internal bool ObserveCharacterData => CharacterData ?? (CharacterDataOldValue != null);
    }
    /// <summary>DOM § 4.3.3 immutable mutation record with static added/removed lists.</summary>
    public sealed class MutationRecord
    {
        internal MutationRecord(string type, Node target, IReadOnlyList<Node>? added, IReadOnlyList<Node>? removed, Node? previous, Node? next, string? name, string? ns, string? old)
        { Type = type; Target = target; AddedNodes = new StaticNodeList(added ?? System.Array.Empty<Node>()); RemovedNodes = new StaticNodeList(removed ?? System.Array.Empty<Node>()); PreviousSibling = previous; NextSibling = next; AttributeName = name; AttributeNamespace = ns; OldValue = old; }
        public string Type { get; }
        public Node Target { get; }
        public NodeList AddedNodes { get; }
        public NodeList RemovedNodes { get; }
        public Node? PreviousSibling { get; }
        public Node? NextSibling { get; }
        public string? AttributeName { get; }
        public string? AttributeNamespace { get; }
        public string? OldValue { get; }
    }
    internal sealed class RegisteredObserver
    {
        internal RegisteredObserver(MutationObserver observer, MutationObserverInit options, RegisteredObserver? source = null)
        { Observer = observer; Options = options; Source = source; }
        internal MutationObserver Observer { get; }
        internal MutationObserverInit Options { get; set; }
        internal RegisteredObserver? Source { get; }
    }
    public partial class Document
    {
        internal HashSet<MutationObserver> PendingObservers { get; } = new();
        internal HashSet<HTMLSlotElement> PendingSlots { get; } = new();
    }
}
