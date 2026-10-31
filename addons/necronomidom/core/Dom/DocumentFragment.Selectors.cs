namespace NecronomiDom.Dom
{
    public partial class DocumentFragment
    {
        /// <summary>DOM ParentNode selector query over this fragment's descendants; no element scope exists.</summary>
        public Element? QuerySelector(string selectors) => SelectorQueries.First(this, selectors, null);
        public NodeList QuerySelectorAll(string selectors) => SelectorQueries.All(this, selectors, null);
    }
}
