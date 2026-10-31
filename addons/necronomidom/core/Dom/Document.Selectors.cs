namespace NecronomiDom.Dom
{
    public partial class Document
    {
        /// <summary>DOM ParentNode selector query over this document's descendants.</summary>
        public Element? QuerySelector(string selectors) => SelectorQueries.First(this, selectors, DocumentElement);
        public NodeList QuerySelectorAll(string selectors) => SelectorQueries.All(this, selectors, DocumentElement);
    }
}
