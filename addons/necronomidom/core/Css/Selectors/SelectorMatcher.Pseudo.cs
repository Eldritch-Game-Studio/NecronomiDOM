using System;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorMatcher
    {
        private static bool MatchesPseudoClass(Element e, PseudoClassSelector pseudo, SelectorMatchContext context)
        {
            var nested = context.PseudoElement == null ? context : context with { PseudoElement = null };
            switch (pseudo.Name)
            {
                case "is": case "where": return pseudo.Arguments != null && Matches(e, pseudo.Arguments, nested);
                case "not": return pseudo.Arguments != null && !Matches(e, pseudo.Arguments, nested);
                case "has": return pseudo.Arguments != null && HasRelative(e, pseudo.Arguments, nested);
                case "root": return ReferenceEquals(e.NodeDocument.DocumentElement, e);
                case "scope": return ReferenceEquals(context.Scope, e);
                case "empty":
                    for (var node = e.FirstChild; node != null; node = node.NextSibling)
                        if (node is Element || node is Text { Length: > 0 }) return false;
                    return true;
                case "first-child": return e.ParentNode != null && e.PreviousElementSibling == null;
                case "last-child": return e.ParentNode != null && e.NextElementSibling == null;
                case "only-child": return e.ParentNode != null && e.PreviousElementSibling == null && e.NextElementSibling == null;
                case "first-of-type": return SiblingIndex(e, false, true, null, nested) == 1;
                case "last-of-type": return SiblingIndex(e, true, true, null, nested) == 1;
                case "only-of-type": return SiblingIndex(e, false, true, null, nested) == 1 && SiblingIndex(e, true, true, null, nested) == 1;
                case "nth-child": case "nth-last-child": case "nth-of-type": case "nth-last-of-type":
                    if (pseudo.Nth == null || pseudo.Nth.Of != null && !Matches(e, pseudo.Nth.Of, nested)) return false;
                    int index = SiblingIndex(e, pseudo.Name.Contains("last", StringComparison.Ordinal), pseudo.Name.Contains("of-type", StringComparison.Ordinal), pseudo.Nth.Of, nested);
                    return index > 0 && (pseudo.Nth.A == 0 ? index == pseudo.Nth.B : (index - pseudo.Nth.B) % pseudo.Nth.A == 0 && (index - pseudo.Nth.B) / pseudo.Nth.A >= 0);
                case "lang": return MatchesLanguage(e, pseudo);
                case "dir": return Names.AsciiEqual(Direction(e), (pseudo.Argument ?? string.Empty).Trim());
                case "defined": return e.IsDefined;
                case "state": return e is HTMLElement html && html.HasCustomState(pseudo.DecodedArgument ?? pseudo.Argument ?? "");
                case "host": return context.ShadowScope != null && ReferenceEquals(context.ShadowScope.Host, e)
                    && (pseudo.Arguments == null || Matches(e, pseudo.Arguments, nested));
                case "host-context":
                    if (context.ShadowScope == null || !ReferenceEquals(context.ShadowScope.Host, e) || pseudo.Arguments == null) return false;
                    for (Element? n = e; n != null; n = n.ParentElement)
                        if (Matches(n, pseudo.Arguments, nested)) return true;
                    return false;
                default: return MatchesState(e, pseudo.Name, context);
            }
        }

        private static int SiblingIndex(Element e, bool reverse, bool ofType, SelectorList? of, SelectorMatchContext context)
        {
            if (e.ParentNode == null) return 0;
            int index = 1;
            for (var n = reverse ? e.NextElementSibling : e.PreviousElementSibling; n != null; n = reverse ? n.NextElementSibling : n.PreviousElementSibling)
            {
                if (ofType && (n.LocalName != e.LocalName || n.NamespaceURI != e.NamespaceURI)) continue;
                if (of != null && !Matches(n, of, context)) continue;
                index++;
            }
            return index;
        }

        private static bool HasRelative(Element anchor, SelectorList relative, SelectorMatchContext context)
        {
            var scoped = context with { Scope = anchor };
            foreach (var selector in relative.Selectors)
            {
                Node root = selector.LeadingCombinator is Combinator.AdjacentSibling or Combinator.GeneralSibling
                    ? anchor.ParentNode ?? anchor : anchor;
                for (Node? n = root.FirstChild; n != null; n = n.NextInTreeOrder(root))
                    if (n is Element candidate && MatchesComplex(candidate, selector, selector.Compounds.Count - 1, scoped)) return true;
            }
            return false;
        }

        private static bool MatchesPseudoElement(Element e, PseudoElementSelector pseudo, SelectorMatchContext context)
        {
            if (pseudo.Name == "slotted") return context.MatchShadowElements && e.AssignedSlotInternal != null
                && ReferenceEquals(e.AssignedSlotInternal.Root, context.TreeScope) && pseudo.Arguments != null
                && Matches(e, pseudo.Arguments, context with { PseudoElement = null, ShadowScope = null, TreeScope = e.Root, MatchShadowElements = false });
            if (pseudo.Name == "part") return MatchesExposedPart(e, pseudo.Argument ?? "", context);
            return string.Equals(context.PseudoElement?.TrimStart(':'), pseudo.Name, StringComparison.Ordinal);
        }


        private static string Direction(Element e) => UnicodeDirection.Direction(e);
    }
}
