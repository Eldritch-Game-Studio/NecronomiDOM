using System;
using System.Collections.ObjectModel;
using NecronomiDom.Css.Om;

namespace NecronomiDom.Dom
{
    /// <summary>CSSOM adopted sheet sequence with mutation notifications to the pure style pipeline.</summary>
    internal sealed class StyleSheetCollection : Collection<CSSStyleSheet>
    {
        private readonly Node _owner;
        internal StyleSheetCollection(Node owner) => _owner = owner;
        protected override void InsertItem(int index, CSSStyleSheet item)
        { ArgumentNullException.ThrowIfNull(item); base.InsertItem(index, item); Notify(); }
        protected override void SetItem(int index, CSSStyleSheet item)
        { ArgumentNullException.ThrowIfNull(item); base.SetItem(index, item); Notify(); }
        protected override void RemoveItem(int index) { base.RemoveItem(index); Notify(); }
        protected override void ClearItems() { base.ClearItems(); Notify(); }
        private void Notify() => _owner.NodeDocument.ChangeSink?.ChildrenChanged(_owner);
    }
}
