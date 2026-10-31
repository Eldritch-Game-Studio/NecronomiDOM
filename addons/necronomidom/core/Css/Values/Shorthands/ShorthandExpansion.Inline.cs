using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    internal static partial class ShorthandExpansion
    {
        // CSS Inline 3: the first/last component sets baseline-source, not alignment-baseline.
        private static bool VerticalAlign(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            var assigned = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ComponentValue token in TokenText.WithoutWhitespace(tokens))
            {
                string? target = token is PreservedToken p && (p.Token.IsIdent("first") || p.Token.IsIdent("last"))
                    ? "baseline-source" : null;
                if (target == null)
                {
                    foreach (string name in new[] { "alignment-baseline", "baseline-shift" })
                        if (CssPropertyParser.Parse(name, new[] { token }) != null) { target = name; break; }
                }
                if (target == null || !assigned.Add(target) || !ParsePut(values, target, new[] { token })) return false;
            }
            return assigned.Count != 0;
        }

        private static bool IntrinsicSize(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            tokens = TokenText.WithoutWhitespace(tokens);
            var groups = new List<IReadOnlyList<ComponentValue>>(2);
            for (int index = 0; index < tokens.Count;)
            {
                var group = new List<ComponentValue>(2);
                if (tokens[index] is PreservedToken p && p.Token.IsIdent("auto")) group.Add(tokens[index++]);
                if (index >= tokens.Count) return false;
                group.Add(tokens[index++]);
                groups.Add(group);
            }
            if (groups.Count is < 1 or > 2) return false;
            return ParsePut(values, "contain-intrinsic-width", groups[0])
                && ParsePut(values, "contain-intrinsic-height", groups[groups.Count - 1]);
        }
    }
}
