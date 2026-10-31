using System;
using System.Collections;
using System.Collections.Generic;
using NecronomiDom.Css.Om;

namespace NecronomiDom.Dom
{
    /// <summary>CSSOM § 6.2 live document list; imported external sheets share DOM source order with style blocks.</summary>
    internal sealed class DocumentStyleSheetList : IList<CSSStyleSheet>
    {
        private readonly Node _root;
        private readonly List<CSSStyleSheet> _external = new(), _live = new();
        internal DocumentStyleSheetList(Node root) => _root = root;
        internal IReadOnlyList<CSSStyleSheet> Snapshot() { Refresh(); return _live; }
        private void Refresh()
        {
            _live.Clear();
            for (Node? node = _root.FirstChild; node != null; node = node.NextInTreeOrder(_root))
            {
                if (node is HTMLStyleElement { Sheet: { } inline }) AddUnique(inline);
                for (int i = 0; i < _external.Count; i++)
                    if (ReferenceEquals(_external[i].OwnerNode, node)) AddUnique(_external[i]);
            }
            for (int i = 0; i < _external.Count; i++) if (_external[i].OwnerNode == null) AddUnique(_external[i]);
        }
        private void AddUnique(CSSStyleSheet sheet) { if (!_live.Contains(sheet)) _live.Add(sheet); }
        private void Changed() => _root.NodeDocument.ChangeSink?.ChildrenChanged(_root);
        public int Count { get { Refresh(); return _live.Count; } }
        public bool IsReadOnly => false;
        public CSSStyleSheet this[int index]
        {
            get { Refresh(); return _live[index]; }
            set { Refresh(); var old = _live[index]; int position = _external.IndexOf(old); if (position < 0) throw new InvalidOperationException("DOM-owned style sheets are changed through their owner node."); _external[position] = value; Changed(); }
        }
        public void Add(CSSStyleSheet sheet) { if (!_external.Contains(sheet)) { _external.Add(sheet); Changed(); } }
        public void Clear() { if (_external.Count == 0) return; _external.Clear(); Changed(); }
        public bool Contains(CSSStyleSheet sheet) { Refresh(); return _live.Contains(sheet); }
        public void CopyTo(CSSStyleSheet[] array, int arrayIndex) { Refresh(); _live.CopyTo(array, arrayIndex); }
        public IEnumerator<CSSStyleSheet> GetEnumerator() { Refresh(); return _live.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(CSSStyleSheet sheet) { Refresh(); return _live.IndexOf(sheet); }
        public void Insert(int index, CSSStyleSheet sheet)
        {
            Refresh();
            if (index == _live.Count) { Add(sheet); return; }
            int externalIndex = _external.IndexOf(_live[index]);
            if (externalIndex < 0) throw new InvalidOperationException("DOM-owned style sheet order follows the owner nodes.");
            _external.Insert(externalIndex, sheet); Changed();
        }
        public bool Remove(CSSStyleSheet sheet) { bool removed = _external.Remove(sheet); if (removed) Changed(); return removed; }
        public void RemoveAt(int index) { Refresh(); if (!Remove(_live[index])) throw new InvalidOperationException("Remove the stylesheet's owner node instead."); }
    }
}
