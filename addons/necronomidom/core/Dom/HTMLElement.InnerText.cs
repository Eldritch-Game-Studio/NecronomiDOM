namespace NecronomiDom.Dom
{
    public partial class Document
    {
        internal string? ReadRenderedText(HTMLElement element) => _defaultView?.RenderedText?.Invoke(element);
    }
    public partial class HTMLElement
    {
        /// <summary>HTML § 3.2.7 rendered-text getter/setter. The host supplies rendered text; absent render trees use DOM textContent.</summary>
        public string InnerText
        {
            get => NodeDocument.ReadRenderedText(this) ?? TextContent ?? string.Empty;
            set
            {
                string text = value ?? string.Empty;
                var fragment = NodeDocument.CreateDocumentFragment();
                int start = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] is not ('\r' or '\n')) continue;
                    if (i > start) fragment.AppendChild(NodeDocument.CreateTextNode(text[start..i]));
                    fragment.AppendChild(NodeDocument.CreateElement("br"));
                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    start = i + 1;
                }
                if (start < text.Length) fragment.AppendChild(NodeDocument.CreateTextNode(text[start..]));
                ReplaceAllChildren(fragment);
            }
        }
    }
}
