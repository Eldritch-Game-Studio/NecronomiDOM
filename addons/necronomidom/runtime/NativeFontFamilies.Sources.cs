using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Runtime;

public sealed partial class NativeFontFamilies
{
    private readonly List<(CSSStyleSheet Sheet, long Revision)> _fontSources = new();
    private Document? _fontDocument;
    private string? _fontBaseUrl;
    private int _documentSheetCount;
    private bool FontSourcesUnchanged(Document document)
    {
        if (!ReferenceEquals(_fontDocument, document) || _fontBaseUrl != document.BaseUrl?.Href ||
            document.StyleSheets.Count != _documentSheetCount ||
            _fontSources.Count != _documentSheetCount + document.AdoptedStyleSheets.Count) return false;
        for (int i = 0; i < _fontSources.Count; i++)
        {
            var sheet = i < _documentSheetCount ? document.StyleSheets[i] : document.AdoptedStyleSheets[i - _documentSheetCount];
            if (!ReferenceEquals(sheet, _fontSources[i].Sheet) || sheet.Revision != _fontSources[i].Revision) return false;
        }
        return true;
    }
    private void CaptureFontSources(Document document)
    {
        _fontSources.Clear(); _fontDocument = document; _fontBaseUrl = document.BaseUrl?.Href;
        _documentSheetCount = document.StyleSheets.Count;
        foreach (var sheet in document.StyleSheets) _fontSources.Add((sheet, sheet.Revision));
        foreach (var sheet in document.AdoptedStyleSheets) _fontSources.Add((sheet, sheet.Revision));
    }
    private void ClearFontSources()
    { _fontSources.Clear(); _fontDocument = null; _fontBaseUrl = null; _documentSheetCount = 0; }
}
