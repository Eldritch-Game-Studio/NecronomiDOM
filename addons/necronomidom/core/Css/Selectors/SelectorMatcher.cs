using System;
using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Selectors
{
    /// <summary>Selectors 4 matching context: tree scope, namespace bindings, nesting parent and host-provided dynamic states.</summary>
    public sealed record SelectorMatchContext(Element? Scope = null, SelectorList? NestingParent = null,
        IReadOnlyDictionary<string, string>? Namespaces = null, Func<Element, string, bool>? State = null,
        string? PseudoElement = null, ShadowRoot? ShadowScope = null, Node? TreeScope = null, bool MatchShadowElements = false);

    /// <summary>Selectors 4 § 19 right-to-left selector matching against the NecronomiDOM DOM, without a native browser.</summary>
    public static partial class SelectorMatcher
    {
        public static bool Matches(Element element, SelectorList selectors, SelectorMatchContext? context = null)
        {
            context ??= new SelectorMatchContext(element.OwnerDocument?.DocumentElement);
            foreach (var selector in selectors.Selectors)
                if (MatchesComplex(element, selector, selector.Compounds.Count - 1, context)) return true;
            return false;
        }

        internal static bool MatchesComplex(Element element, ComplexSelector selector, int index, SelectorMatchContext context)
        {
            if (context.TreeScope != null && !ReferenceEquals(element.Root, context.TreeScope)
                && !(context.ShadowScope != null && ReferenceEquals(context.ShadowScope.Host, element))
                && OriginatingElement(element, selector.Compounds[index], context) == null) return false;
            if (!MatchesCompound(element, selector.Compounds[index], context)) return false;
            element = OriginatingElement(element, selector.Compounds[index], context) ?? element;
            context = context.PseudoElement == null ? context : context with { PseudoElement = null };
            if (index == 0) return selector.LeadingCombinator == null || MatchesLeading(element, selector.LeadingCombinator.Value, context.Scope);
            switch (selector.Combinators[index - 1])
            {
                case Combinator.Child:
                    return SelectorParent(element, context) is { } parent && MatchesComplex(parent, selector, index - 1, context);
                case Combinator.AdjacentSibling:
                    return element.PreviousElementSibling is { } previous && MatchesComplex(previous, selector, index - 1, context);
                case Combinator.GeneralSibling:
                    for (var n = element.PreviousElementSibling; n != null; n = n.PreviousElementSibling)
                        if (MatchesComplex(n, selector, index - 1, context)) return true;
                    return false;
                case Combinator.Descendant:
                    for (var n = SelectorParent(element, context); n != null; n = SelectorParent(n, context))
                        if (MatchesComplex(n, selector, index - 1, context)) return true;
                    return false;
                default:
                    return false;
            }
        }

        private static bool MatchesLeading(Element element, Combinator combinator, Element? anchor)
        {
            if (anchor == null) return false;
            if (combinator == Combinator.Child) return ReferenceEquals(element.ParentElement, anchor);
            if (combinator == Combinator.AdjacentSibling) return ReferenceEquals(element.PreviousElementSibling, anchor);
            if (combinator == Combinator.GeneralSibling)
            {
                for (var n = element.PreviousElementSibling; n != null; n = n.PreviousElementSibling)
                    if (ReferenceEquals(n, anchor)) return true;
                return false;
            }
            return combinator == Combinator.Descendant && !ReferenceEquals(element, anchor) && anchor.Contains(element);
        }

        private static bool MatchesCompound(Element element, CompoundSelector compound, SelectorMatchContext context)
        {
            bool hasPseudo = false;
            Element original = element;
            element = OriginatingElement(element, compound, context) ?? element;
            if (context.ShadowScope != null && ReferenceEquals(context.ShadowScope.Host, element) && !ContainsHostSelector(compound)) return false;
            if (compound.NamespaceURI != null && (element.NamespaceURI ?? "") != compound.NamespaceURI
                && !(context.ShadowScope != null && ReferenceEquals(context.ShadowScope.Host, element))) return false;
            foreach (SimpleSelector selector in compound.SimpleSelectors)
            {
                bool match = selector switch
                {
                    TypeSelector type => (type.NamespaceURI == null || type.NamespaceURI == (element.NamespaceURI ?? ""))
                        && NameEquals(element, element.LocalName, type.Name),
                    UniversalSelector universal => universal.NamespaceURI == null || universal.NamespaceURI == (element.NamespaceURI ?? ""),
                    IdSelector id => AttributeEqual(element.Id.AsSpan(), id.Id.AsSpan(), element.NodeDocument.CompatMode == "BackCompat"),
                    ClassSelector cls => TokenContains(element.ClassName, cls.ClassName, element.NodeDocument.CompatMode == "BackCompat"),
                    AttributeSelector attribute => MatchesAttribute(element, attribute, context),
                    PseudoClassSelector pseudo => MatchesPseudoClass(element, pseudo, context),
                    PseudoElementSelector pseudo => MatchesPseudoElement(original, pseudo, context),
                    NestingSelector => context.NestingParent == null ? ReferenceEquals(context.Scope, element) : Matches(element, context.NestingParent,
                        context.PseudoElement == null ? context : context with { PseudoElement = null }),
                    _ => false,
                };
                if (!match) return false;
                hasPseudo |= selector is PseudoElementSelector;
                if (selector is PseudoElementSelector { Name: "part" or "slotted" }) element = original;
            }
            return context.PseudoElement == null || hasPseudo;
        }


        private static bool NameEquals(Element element, string first, string second)
            => element.NamespaceURI == "http://www.w3.org/1999/xhtml" && element.NodeDocument.IsHtmlDocument
                ? Names.AsciiEqual(first, second) : string.Equals(first, second, StringComparison.Ordinal);

        private static bool MatchesAttribute(Element element, AttributeSelector selector, SelectorMatchContext context)
        {
            foreach (Attr attr in element.Attributes)
            {
                if (!NameEquals(element, attr.LocalName, selector.Name)) continue;
                if (selector.NamespaceURI != null && selector.NamespaceURI != (attr.NamespaceURI ?? "")) continue;
                if (selector.Operator == AttributeOperator.Exists) return true;
                bool insensitive = selector.CaseSensitivity == CaseSensitivity.Insensitive
                    || selector.CaseSensitivity == CaseSensitivity.Default && IsHtmlInsensitiveAttribute(element, attr.LocalName);
                string value = attr.Value;
                string expected = selector.Value ?? string.Empty;
                switch (selector.Operator)
                {
                    case AttributeOperator.Equals: if (AttributeEqual(value.AsSpan(), expected.AsSpan(), insensitive)) return true; break;
                    case AttributeOperator.Includes:
                        if (expected.Length > 0 && !HasSpace(expected) && TokenContains(value, expected, insensitive)) return true;
                        break;
                    case AttributeOperator.DashMatch:
                        if (AttributeEqual(value.AsSpan(), expected.AsSpan(), insensitive)
                            || value.Length > expected.Length && value[expected.Length] == '-' && AttributePrefix(value, expected, insensitive)) return true;
                        break;
                    case AttributeOperator.Prefix: if (expected.Length > 0 && AttributePrefix(value, expected, insensitive)) return true; break;
                    case AttributeOperator.Suffix: if (expected.Length > 0 && expected.Length <= value.Length
                        && AttributeEqual(value.AsSpan(value.Length - expected.Length), expected.AsSpan(), insensitive)) return true; break;
                    case AttributeOperator.Substring: if (expected.Length > 0 && AttributeIndex(value, expected, insensitive) >= 0) return true; break;
                }
            }
            return false;
        }

        private static bool TokenContains(string value, string token, bool insensitive)
        {
            int start = 0;
            while (start < value.Length)
            {
                while (start < value.Length && IsSpace(value[start])) start++;
                int end = start;
                while (end < value.Length && !IsSpace(value[end])) end++;
                if (AttributeEqual(value.AsSpan(start, end - start), token.AsSpan(), insensitive)) return true;
                start = end;
            }
            return false;
        }
        private static bool HasSpace(string value) { foreach (char c in value) if (IsSpace(c)) return true; return false; }
        private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';
        private static bool IsHtmlInsensitiveAttribute(Element element, string name)
            => element.NamespaceURI == "http://www.w3.org/1999/xhtml" && name is "type" or "dir" or "lang" or "rel" or "target" or "method" or "enctype" or "align" or "valign" or "autocomplete" or "crossorigin" or "charset" or "http-equiv" or "contenteditable" or "draggable" or "spellcheck" or "translate";
    }
}
