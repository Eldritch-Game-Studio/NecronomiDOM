using System;
using System.Collections;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4.13.7.5 / Web IDL writable setlike&lt;DOMString&gt; with exact strings and live insertion-order iteration.</summary>
    public sealed class CustomStateSet : IReadOnlyCollection<string>
    {
        private readonly HTMLElement _target;
        private readonly Dictionary<string, LinkedListNode<string>> _lookup = new(StringComparer.Ordinal);
        private readonly LinkedList<string> _values = new();
        private readonly List<WeakReference<StateIterator>> _iterators = new();
        internal CustomStateSet(HTMLElement target) => _target = target;
        public int Count => _lookup.Count;
        /// <summary>Web IDL setlike add: returns this set, without a legacy dashed-identifier restriction.</summary>
        public CustomStateSet Add(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_lookup.ContainsKey(value)) return this;
            var node = _values.AddLast(value); _lookup.Add(value, node);
            AdjustIterators(null, node, false); _target.NotifyStateChanged(); return this;
        }
        public bool Has(string value) { ArgumentNullException.ThrowIfNull(value); return _lookup.ContainsKey(value); }
        public bool Delete(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_lookup.Remove(value, out var node)) return false;
            var next = node.Next; _values.Remove(node);
            AdjustIterators(node, next, false); _target.NotifyStateChanged(); return true;
        }
        public void Clear()
        {
            if (_lookup.Count == 0) return;
            _lookup.Clear(); _values.Clear(); AdjustIterators(null, null, true); _target.NotifyStateChanged();
        }
        public IEnumerable<string> Keys() => this;
        public IEnumerable<string> Values() => this;
        public IEnumerable<KeyValuePair<string, string>> Entries()
        {
            using var iterator = GetEnumerator();
            while (iterator.MoveNext()) yield return new KeyValuePair<string, string>(iterator.Current, iterator.Current);
        }
        public void ForEach(Action<string, string, CustomStateSet> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            using var iterator = GetEnumerator();
            while (iterator.MoveNext()) callback(iterator.Current, iterator.Current, this);
        }
        public IEnumerator<string> GetEnumerator()
        {
            AdjustIterators(null, null, false);
            var iterator = new StateIterator(this, _values.First); _iterators.Add(new WeakReference<StateIterator>(iterator)); return iterator;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private void AdjustIterators(LinkedListNode<string>? removed, LinkedListNode<string>? next, bool clear)
        {
            for (var i = _iterators.Count - 1; i >= 0; i--)
            {
                if (!_iterators[i].TryGetTarget(out var iterator) || iterator.Done) { _iterators.RemoveAt(i); continue; }
                if (clear || ReferenceEquals(iterator.NextNode, removed)) iterator.NextNode = next;
            }
        }
        private sealed class StateIterator : IEnumerator<string>
        {
            private string? _current;
            private readonly CustomStateSet _owner;
            internal StateIterator(CustomStateSet owner, LinkedListNode<string>? next) { _owner = owner; NextNode = next; }
            internal LinkedListNode<string>? NextNode { get; set; }
            internal bool Done { get; private set; }
            public string Current => _current ?? throw new InvalidOperationException("Iterator has no current state.");
            object IEnumerator.Current => Current;
            public bool MoveNext()
            {
                if (Done) return false;
                if (NextNode is not { } node) { Dispose(); return false; }
                _current = node.Value; NextNode = node.Next; return true;
            }
            public void Dispose() { Done = true; NextNode = null; _current = null; _owner.AdjustIterators(null, null, false); }
            public void Reset() => throw new NotSupportedException("Custom state iterators cannot be reset.");
        }
    }
}
