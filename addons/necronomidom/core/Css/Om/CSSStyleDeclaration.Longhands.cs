using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    public sealed partial class CSSStyleDeclaration
    {
        private static readonly ConcurrentDictionary<PropertyDefinition, string[]> LonghandCache = new();

        // CSSOM declaration storage contains canonical leaf names, including shorthand resets,
        // not the webref definition's aliases or intermediate shorthand children.
        private static IReadOnlyList<string> Longhands(PropertyDefinition definition) => LonghandCache.GetOrAdd(definition, static property =>
        {
            var names = new List<string>();
            foreach (var name in ShorthandExpansion.Leaves(property)) names.Add(name);
            return names.ToArray();
        });
    }
}
