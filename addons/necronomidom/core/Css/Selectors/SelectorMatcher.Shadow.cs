using System;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorMatcher
    {
        private static Element? SelectorParent(Element element, SelectorMatchContext context)
            => element.ParentElement ?? (element.ParentNode is ShadowRoot shadow && ReferenceEquals(shadow, context.ShadowScope) ? shadow.Host : null);
        private static bool ContainsHostSelector(CompoundSelector compound)
        {
            var simples = compound.SimpleSelectors;
            for (int i = 0; i < simples.Count; i++)
                if (simples[i] is PseudoClassSelector pseudo)
                {
                    if (pseudo.Name is "host" or "host-context") return true;
                    if (pseudo.Name is "is" or "where" && pseudo.Arguments != null)
                    {
                        var nested = pseudo.Arguments.Selectors;
                        for (int j = 0; j < nested.Count; j++)
                        {
                            var compounds = nested[j].Compounds;
                            for (int k = 0; k < compounds.Count; k++) if (ContainsHostSelector(compounds[k])) return true;
                        }
                    }
                }
            return false;
        }
        private static Element? OriginatingElement(Element element, CompoundSelector compound, SelectorMatchContext context)
        {
            if (!context.MatchShadowElements) return null;
            var simples = compound.SimpleSelectors;
            for (int i = 0; i < simples.Count; i++)
            {
                if (simples[i] is PseudoElementSelector { Name: "slotted" }) return element.AssignedSlotInternal;
                if (simples[i] is PseudoElementSelector { Name: "part" }) return BoundaryShadow(element.Root, context.TreeScope)?.Host;
            }
            return null;
        }
        internal static bool IsShadowDescendantOfScope(Element element, Node scope) => BoundaryShadow(element.Root, scope) != null;
        private static ShadowRoot? BoundaryShadow(Node root, Node? target)
        {
            while (root is ShadowRoot shadow)
            {
                if (ReferenceEquals(shadow.Host.Root, target)) return shadow;
                root = shadow.Host.Root;
            }
            return null;
        }
        private static bool MatchesExposedPart(Element element, string name, SelectorMatchContext context)
        {
            if (!context.MatchShadowElements || context.TreeScope == null) return false;
            ShadowRoot? boundary = BoundaryShadow(element.Root, context.TreeScope);
            return boundary != null && NameInShadow(element, name.AsSpan(), boundary);
        }
        private static bool NameInShadow(Element element, ReadOnlySpan<char> name, ShadowRoot scope)
        {
            if (ReferenceEquals(element.Root, scope)) return PartTokenContains(element.GetAttribute("part") ?? "", name);
            ShadowRoot? nested = BoundaryShadow(element.Root, scope);
            if (nested == null) return false;
            ReadOnlySpan<char> exports = (nested.Host.GetAttribute("exportparts") ?? "").AsSpan();
            while (!exports.IsEmpty)
            {
                int comma = exports.IndexOf(',');
                ReadOnlySpan<char> pair = (comma < 0 ? exports : exports[..comma]).Trim();
                int colon = pair.IndexOf(':');
                ReadOnlySpan<char> inner = (colon < 0 ? pair : pair[..colon]).Trim();
                ReadOnlySpan<char> outer = (colon < 0 ? pair : pair[(colon + 1)..]).Trim();
                if (!inner.IsEmpty && !HasPartSpace(inner) && !HasPartSpace(outer) && outer.SequenceEqual(name)
                    && NameInShadow(element, inner, nested)) return true;
                if (comma < 0) break;
                exports = exports[(comma + 1)..];
            }
            return false;
        }
        private static bool HasPartSpace(ReadOnlySpan<char> text) { foreach (char c in text) if (IsSpace(c)) return true; return false; }
        private static bool PartTokenContains(string text, ReadOnlySpan<char> name)
        {
            int start = 0;
            while (start < text.Length)
            {
                while (start < text.Length && IsSpace(text[start])) start++;
                int end = start;
                while (end < text.Length && !IsSpace(text[end])) end++;
                if (text.AsSpan(start, end - start).SequenceEqual(name)) return true;
                start = end;
            }
            return false;
        }
    }
}
