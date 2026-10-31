using System;
using NecronomiDom.Css.Om;

namespace NecronomiDom.Dom
{
    public partial class HTMLStyleElement
    {
        private CSSStyleSheet? _sheet;
        private string? _sheetText;
        private string? _sheetMedia;
        /// <summary>HTML § 4.2.6 style block sheet, retaining CSSOM mutations when text has not changed.</summary>
        public CSSStyleSheet? Sheet
        {
            get
            {
                string type = GetAttribute("type") ?? "";
                if (type.Length > 0 && !Names.AsciiEqual(type, "text/css")) return null;
                string text = ReadStyleText();
                string media = GetAttribute("media") ?? "";
                if (_sheet == null || text != _sheetText)
                {
                    _sheetText = text;
                    _sheet = CSSStyleSheet.Parse(text, BaseURI, NodeDocument.BaseUrl, null);
                    _sheet.SetInlineOwner(this);
                }
                if (_sheetMedia != media) { _sheetMedia = media; _sheet.Media.MediaText = media; }
                return _sheet;
            }
        }
        private string ReadStyleText()
        {
            if (FirstChild == null) return "";
            if (FirstChild is Text only && only.NextSibling == null) return only.Data;
            if (_sheetText != null)
            {
                int offset = 0;
                for (Node? node = FirstChild; node != null; node = node.NextInTreeOrder(this))
                    if (node is Text text)
                    {
                        string data = text.Data;
                        if (data.Length > _sheetText.Length - offset ||
                            !_sheetText.AsSpan(offset, data.Length).SequenceEqual(data.AsSpan())) return TextContent ?? "";
                        offset += data.Length;
                    }
                if (offset == _sheetText.Length) return _sheetText;
            }
            return TextContent ?? "";
        }
    }
}
