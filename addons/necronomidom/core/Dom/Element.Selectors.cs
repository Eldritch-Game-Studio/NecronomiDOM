using System.Collections.Generic;
using NecronomiDom.Css.Selectors;

namespace NecronomiDom.Dom
{
    public partial class Element
    {
        /// <summary>DOM § 4.9 matches(): invalid selector syntax throws SyntaxError.</summary>
        public bool Matches(string selectors) => SelectorMatcher.Matches(this, SelectorQueries.Parse(selectors), new SelectorMatchContext(this));
        /// <summary>DOM § 4.9 closest(): includes this element, then ancestors in the same tree.</summary>
        public Element? Closest(string selectors)
        {
            var parsed = SelectorQueries.Parse(selectors);
            var context = new SelectorMatchContext(this);
            for (Element? element = this; element != null; element = element.ParentElement)
                if (SelectorMatcher.Matches(element, parsed, context)) return element;
            return null;
        }
        public Element? QuerySelector(string selectors) => SelectorQueries.First(this, selectors, this);
        public NodeList QuerySelectorAll(string selectors) => SelectorQueries.All(this, selectors, this);
    }

    internal static class SelectorQueries
    {
        internal static SelectorList Parse(string selectors) => SelectorParser.ParseSelectorList(selectors)
            ?? throw new DomException("The selector is not valid.", "SyntaxError");
        internal static Element? First(Node root, string selectors, Element? scope)
        {
            var parsed = Parse(selectors);
            var context = new SelectorMatchContext(scope);
            for (Node? n = root.FirstChild; n != null; n = n.NextInTreeOrder(root))
                if (n is Element element && SelectorMatcher.Matches(element, parsed, context)) return element;
            return null;
        }
        internal static NodeList All(Node root, string selectors, Element? scope)
        {
            var parsed = Parse(selectors);
            var context = new SelectorMatchContext(scope);
            var result = new List<Node>();
            for (Node? n = root.FirstChild; n != null; n = n.NextInTreeOrder(root))
                if (n is Element element && SelectorMatcher.Matches(element, parsed, context)) result.Add(element);
            return new StaticNodeList(result);
        }
    }
}
