using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 4.3 observers, transient subtree registrations and explicit microtask delivery.</summary>
    public sealed class MutationObserver
    {
        private readonly Action<IReadOnlyList<MutationRecord>, MutationObserver> _callback;
        private readonly List<MutationRecord> _records = new();
        private readonly HashSet<Node> _targets = new();
        public MutationObserver(Action<IReadOnlyList<MutationRecord>, MutationObserver> callback) => _callback = callback;
        public void Observe(Node target, MutationObserverInit options)
        {
            if (!options.ChildList && !options.ObserveAttributes && !options.ObserveCharacterData ||
                options.Attributes == false && (options.AttributeOldValue == true || options.AttributeFilter != null) ||
                options.CharacterData == false && options.CharacterDataOldValue == true)
                throw new DomTypeError("Inconsistent or empty observer options.");
            var filter = options.AttributeFilter == null ? null : new List<string>(options.AttributeFilter);
            var copy = new MutationObserverInit { ChildList = options.ChildList, Attributes = options.ObserveAttributes, CharacterData = options.ObserveCharacterData, Subtree = options.Subtree, AttributeOldValue = options.AttributeOldValue, CharacterDataOldValue = options.CharacterDataOldValue, AttributeFilter = filter };
            target.RegisteredObservers ??= new List<RegisteredObserver>();
            foreach (var registration in target.RegisteredObservers)
                if (ReferenceEquals(registration.Observer, this) && registration.Source == null)
                { RemoveTransients(); registration.Options = copy; return; }
            target.RegisteredObservers.Add(new RegisteredObserver(this, copy)); _targets.Add(target);
        }
        public void Disconnect()
        {
            foreach (var target in _targets) target.RegisteredObservers?.RemoveAll(r => ReferenceEquals(r.Observer, this));
            _targets.Clear(); _records.Clear();
        }
        public IReadOnlyList<MutationRecord> TakeRecords() { var result = _records.ToArray(); _records.Clear(); return result; }
        private void RemoveTransients()
        {
            foreach (var target in _targets) target.RegisteredObservers?.RemoveAll(r => ReferenceEquals(r.Observer, this) && r.Source != null);
            _targets.RemoveWhere(target =>
            {
                if (target.RegisteredObservers != null)
                    foreach (var registration in target.RegisteredObservers) if (ReferenceEquals(registration.Observer, this)) return false;
                return true;
            });
        }
        internal static void AddTransientObservers(Node parent, Node removed)
        {
            for (Node? n = parent; n != null; n = n.ParentNode)
            {
                if (n.RegisteredObservers == null) continue;
                foreach (var r in n.RegisteredObservers)
                {
                    if (!r.Options.Subtree) continue;
                    removed.RegisteredObservers ??= new List<RegisteredObserver>();
                    removed.RegisteredObservers.Add(new RegisteredObserver(r.Observer, r.Options, r.Source ?? r));
                    r.Observer._targets.Add(removed);
                    parent.NodeDocument.PendingObservers.Add(r.Observer);
                }
            }
        }
        internal static void Queue(Node target, string type, IReadOnlyList<Node>? added = null, IReadOnlyList<Node>? removed = null, Node? previous = null, Node? next = null, string? attributeName = null, string? attributeNamespace = null, string? oldValue = null)
        {
            Dictionary<MutationObserver, bool>? interested = null;
            for (Node? node = target; node != null; node = node.ParentNode)
            {
                if (node.RegisteredObservers == null) continue;
                foreach (var registration in node.RegisteredObservers)
                {
                    var o = registration.Options;
                    if (!ReferenceEquals(node, target) && !o.Subtree) continue;
                    if (type == "attributes")
                    {
                        if (!o.ObserveAttributes) continue;
                        if (o.AttributeFilter != null && (attributeNamespace != null || !Contains(o.AttributeFilter, attributeName!))) continue;
                    }
                    if (type == "childList" && !o.ChildList || type == "characterData" && !o.ObserveCharacterData) continue;
                    interested ??= new Dictionary<MutationObserver, bool>();
                    var wantsOld = type == "attributes" ? o.AttributeOldValue == true : type == "characterData" && o.CharacterDataOldValue == true;
                    interested.TryGetValue(registration.Observer, out var existing);
                    interested[registration.Observer] = existing || wantsOld;
                }
            }
            if (interested == null) return;
            foreach (var pair in interested)
            {
                pair.Key._records.Add(new MutationRecord(type, target, added, removed, previous, next, attributeName, attributeNamespace, pair.Value ? oldValue : null));
                target.NodeDocument.PendingObservers.Add(pair.Key);
            }
        }
        private static bool Contains(IReadOnlyList<string> list, string name)
        { foreach (var item in list) if (item == name) return true; return false; }
        public static void NotifyAll(Document document)
        {
            var observers = new List<MutationObserver>(document.PendingObservers); document.PendingObservers.Clear();
            var slots = new List<HTMLSlotElement>(document.PendingSlots); document.PendingSlots.Clear();
            List<Exception>? errors = null;
            foreach (var observer in observers)
            {
                var records = observer.TakeRecords(); observer.RemoveTransients();
                if (records.Count == 0) continue;
                try { observer._callback(records, observer); }
                catch (Exception error)
                {
                    if (document.ListenerExceptionHandler == null) (errors ??= new()).Add(error);
                    else
                    {
                        try { document.ListenerExceptionHandler(error); }
                        catch (Exception reportingError) { (errors ??= new()).Add(reportingError); }
                    }
                }
            }
            foreach (var slot in slots) slot.DispatchEvent(new Event("slotchange", new EventInit { Bubbles = true }));
            if (errors != null) throw new AggregateException(errors);
        }
    }
}
