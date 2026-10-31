using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

/// <summary>Preserves style invalidation; only stylesheet revision counters are read in clean frames.</summary>
public sealed class DocumentInvalidation : IDomChangeSink, IDisposable
{
    private readonly Document _document;
    private readonly IDomChangeSink? _previous;
    private readonly Action _changed;
    private readonly Action<Element>? _inlineStyleChanged;
    private readonly List<(CSSStyleSheet Sheet, long Revision)> _sheets = new();
    public DocumentInvalidation(Document document, Action changed, Action<Element>? inlineStyleChanged = null)
    {
        _document = document; _previous = document.ChangeSink; _changed = changed;
        _inlineStyleChanged = inlineStyleChanged; document.ChangeSink = this;
    }
    public void CaptureStyleSheets()
    {
        _sheets.Clear();
        foreach (var sheet in _document.StyleSheets) _sheets.Add((sheet, sheet.Revision));
        foreach (var sheet in _document.AdoptedStyleSheets) _sheets.Add((sheet, sheet.Revision));
    }
    public bool StyleSheetsChanged()
    {
        foreach (var entry in _sheets) if (entry.Sheet.Revision != entry.Revision) return true;
        return false;
    }
    bool IDomChangeSink.RequiresAttributeOldValue => _previous?.RequiresAttributeOldValue == true;
    void IDomChangeSink.ChildrenChanged(Node parent) { _previous?.ChildrenChanged(parent); _changed(); }
    void IDomChangeSink.AttributeChanged(Element e, string name, string? oldValue)
    {
        _previous?.AttributeChanged(e, name, oldValue);
        if (name == "style" && _inlineStyleChanged != null) _inlineStyleChanged(e);
        else _changed();
    }
    void IDomChangeSink.CharacterDataChanged(CharacterData n) { _previous?.CharacterDataChanged(n); _changed(); }
    void IDomChangeSink.NodeInserted(Node n) { _previous?.NodeInserted(n); _changed(); }
    void IDomChangeSink.NodeRemoved(Node n, Node parent) { _previous?.NodeRemoved(n, parent); _changed(); }
    public void Dispose() { if (ReferenceEquals(_document.ChangeSink, this)) _document.ChangeSink = _previous; }
}
