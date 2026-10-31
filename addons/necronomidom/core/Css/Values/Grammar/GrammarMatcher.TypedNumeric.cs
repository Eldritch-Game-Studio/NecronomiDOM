using System.Collections.Generic;

namespace NecronomiDom.Css.Values.Grammar
{
    internal sealed partial class GrammarMatcher
    {
        // Typed OM compiles one-component numeric alternatives from the same production graph as string parsing.
        internal IEnumerable<GrammarNode> ResolveTypedReference(TypeNode node) => Resolve(node);
    }
}
