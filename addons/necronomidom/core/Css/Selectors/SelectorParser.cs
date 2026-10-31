using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Selectors
{
    /// <summary>Selectors 4 § 15 grammar parser, including relative and forgiving selector lists.</summary>
    public static partial class SelectorParser
    {
        public static SelectorList? ParseSelectorList(string css, SelectorParseOptions? options = null)
        {
            var diagnostics = new List<WebDiagnostic>();
            var values = CssParser.ParseComponentValueList(css, diagnostics, "", out var source);
            foreach (var diagnostic in diagnostics) if (diagnostic.Code == "css-unclosed-block") return null;
            return ParseSelectorList(values, (options ?? new SelectorParseOptions()) with { SourceText = source });
        }
        public static SelectorList? ParseSelectorList(IReadOnlyList<ComponentValue> values, SelectorParseOptions options)
        {
            if (!ValidAnyValue(values)) return null;
            var selectors = new List<ComplexSelector>();
            foreach (var entry in CssParser.SplitComma(values))
            {
                var trimmed = CssParser.Trim(entry);
                var reader = new SelectorReader(trimmed, options);
                var selector = reader.Complex();
                if (selector != null && (options.CompoundOnly && selector.Compounds.Count != 1 || options.PartArgument && !IsPartSelector(selector))) selector = null;
                if (selector == null)
                {
                    if (!options.Forgiving) return null;
                    selectors.Add(new ComplexSelector(new[] { new CompoundSelector(new SimpleSelector[]
                    { new InvalidSelector(InvalidText(trimmed, options.SourceText)) { ContainsNesting = ContainsNesting(trimmed) } }) }, Array.Empty<Combinator>()));
                }
                else selectors.Add(selector);
            }
            return selectors.Count == 0 && !options.Forgiving ? null : new SelectorList(selectors);
        }
        private sealed partial class SelectorReader
        {
            private readonly IReadOnlyList<ComponentValue> _values;
            private readonly SelectorParseOptions _options;
            private int _p;
            public SelectorReader(IReadOnlyList<ComponentValue> values, SelectorParseOptions options) { _values = values; _options = options; }
            private bool AtEnd => _p >= _values.Count;
            private bool Delim(char c) => !AtEnd && CssParser.IsDelim(_values[_p], c);
            private bool Kind(CssTokenKind kind) => !AtEnd && CssParser.Is(_values[_p], kind);
            private string? Ident()
            {
                if (!Kind(CssTokenKind.Ident)) return null;
                return ((PreservedToken)_values[_p++]).Token.Value;
            }
            private bool Space()
            {
                var start = _p;
                while (Kind(CssTokenKind.Whitespace)) _p++;
                return start != _p;
            }
            private Combinator? ReadCombinator()
            {
                if (Delim('>')) { _p++; return Combinator.Child; }
                if (Delim('+')) { _p++; return Combinator.AdjacentSibling; }
                if (Delim('~')) { _p++; return Combinator.GeneralSibling; }
                if (Delim('|') && _p + 1 < _values.Count && CssParser.IsDelim(_values[_p + 1], '|')) { _p += 2; return Combinator.Column; }
                return null;
            }
            public ComplexSelector? Complex()
            {
                if (AtEnd) return null;
                var leading = ReadCombinator();
                if (leading.HasValue && !_options.Relative) return null;
                if (_options.Relative && !leading.HasValue) leading = Combinator.Descendant;
                Space();
                var compounds = new List<CompoundSelector>();
                var combinators = new List<Combinator>();
                while (!AtEnd)
                {
                    var compound = Compound();
                    if (compound == null) return null;
                    compounds.Add(compound);
                    var spaced = Space();
                    if (AtEnd) break;
                    var combinator = ReadCombinator();
                    if (!combinator.HasValue && !spaced) return null;
                    foreach (var simple in compound.SimpleSelectors) if (simple is PseudoElementSelector) return null;
                    combinators.Add(combinator ?? Combinator.Descendant);
                    Space();
                    if (AtEnd) return null;
                }
                if (_options.IgnoreSubjectDefaultNamespace && compounds.Count > 0)
                {
                    var subject = compounds[compounds.Count - 1];
                    var preserve = false;
                    foreach (var simple in subject.SimpleSelectors) if (simple is UniversalSelector { NamespacePrefix: null, NamespaceURI: not null }) preserve = true;
                    compounds[compounds.Count - 1] = new CompoundSelector(subject.SimpleSelectors) { PreserveUniversal = preserve };
                }
                return compounds.Count > 0 ? new ComplexSelector(compounds, combinators, leading) : null;
            }
            private CompoundSelector? Compound()
            {
                var simples = new List<SimpleSelector>();
                if (ReadType(out var type))
                {
                    if (type == null) return null;
                    simples.Add(type);
                }
                PseudoElementSelector? pseudoElement = null;
                while (!AtEnd)
                {
                    SimpleSelector? simple;
                    if (Kind(CssTokenKind.Hash))
                    {
                        var hash = ((PreservedToken)_values[_p++]).Token;
                        if (!hash.IsIdHash) return null;
                        simple = new IdSelector(hash.Value);
                    }
                    else if (Delim('.')) { _p++; var name = Ident(); if (name == null) return null; simple = new ClassSelector(name); }
                    else if (_values[_p] is SimpleBlock block && block.Opener == CssTokenKind.LeftBracket) { _p++; simple = Attribute(block); }
                    else if (Kind(CssTokenKind.Colon)) simple = Pseudo(pseudoElement);
                    else if (Delim('&') && _options.AllowNesting) { _p++; simple = new NestingSelector(); }
                    else break;
                    if (simple == null) return null;
                    if (pseudoElement != null && !AfterPseudoElement(pseudoElement, simple)) return null;
                    if (simple is PseudoElementSelector element) pseudoElement = element;
                    simples.Add(simple);
                }
                return simples.Count > 0 ? new CompoundSelector(simples) { NamespaceURI = type == null ? ResolveNamespace(null, false) : null } : null;
            }
            private static bool AfterPseudoElement(PseudoElementSelector previous, SimpleSelector simple)
            {
                if (simple is PseudoElementSelector next)
                    return previous.Name is "part" or "slotted" ? next.Name is "before" or "after" or "marker" or "placeholder" or "selection" or "first-line" or "first-letter" or "file-selector-button" :
                        previous.Name is "before" or "after" && next.Name == "marker";
                if (simple is not PseudoClassSelector state || previous.Name == "slotted") return false;
                if (previous.Name == "part")
                    return IsPartState(state);
                return state.Name is "hover" or "active" or "focus" or "focus-visible" or "focus-within" or "enabled" or "disabled" or "checked" or "window-inactive";
            }
            private bool ReadType(out SimpleSelector? result)
            {
                result = null;
                var start = _p;
                var first = Ident();
                if (first == null && Delim('*')) { first = "*"; _p++; }
                string? prefix = null;
                if (Delim('|') && !(_p + 1 < _values.Count && (CssParser.IsDelim(_values[_p + 1], '|') || CssParser.IsDelim(_values[_p + 1], '='))))
                {
                    prefix = first ?? "";
                    _p++;
                    first = Ident();
                    if (first == null && Delim('*')) { first = "*"; _p++; }
                    if (first == null || !NamespaceValid(prefix)) return true;
                }
                if (first == null) { _p = start; return false; }
                var uri = ResolveNamespace(prefix, false);
                if (prefix == "*" && (_options.Namespaces == null || !_options.Namespaces.ContainsKey(""))) prefix = null;
                if (prefix != null && prefix.Length > 0 && prefix != "*" && _options.Namespaces != null && _options.Namespaces.TryGetValue("", out var defaultUri) && uri == defaultUri) prefix = null;
                result = first == "*" ? new UniversalSelector(prefix) { NamespaceURI = uri } : new TypeSelector(first, prefix) { NamespaceURI = uri };
                return true;
            }
            private bool NamespaceValid(string prefix) => prefix.Length == 0 || prefix == "*" || (_options.Namespaces != null && _options.Namespaces.ContainsKey(prefix));
            private string? ResolveNamespace(string? prefix, bool attribute)
            {
                if (prefix == "*") return null;
                if (prefix == "" || (attribute && prefix == null)) return "";
                return _options.Namespaces != null && _options.Namespaces.TryGetValue(prefix ?? "", out var uri) ? uri : null;
            }
            private AttributeSelector? Attribute(SimpleBlock block)
            {
                var reader = new SelectorReader(CssParser.Trim(block.Value), _options);
                var name = reader.Ident();
                string? prefix = null;
                if (name == null && reader.Delim('*')) { name = "*"; reader._p++; }
                if (reader.Delim('|') && !(reader._p + 1 < reader._values.Count && CssParser.IsDelim(reader._values[reader._p + 1], '=')))
                {
                    prefix = name ?? "";
                    reader._p++;
                    name = reader.Ident();
                    if (!reader.NamespaceValid(prefix)) return null;
                }
                if (name == null || name == "*") return null;
                reader.Space();
                if (reader.AtEnd) return new AttributeSelector(name, prefix) { NamespaceURI = reader.ResolveNamespace(prefix, true) };
                var op = AttributeOperator.Equals;
                if (reader.Delim('~')) op = AttributeOperator.Includes;
                else if (reader.Delim('|')) op = AttributeOperator.DashMatch;
                else if (reader.Delim('^')) op = AttributeOperator.Prefix;
                else if (reader.Delim('$')) op = AttributeOperator.Suffix;
                else if (reader.Delim('*')) op = AttributeOperator.Substring;
                if (op != AttributeOperator.Equals) reader._p++;
                if (!reader.Delim('=')) return null;
                reader._p++;
                reader.Space();
                var value = reader.Ident();
                if (value == null && reader.Kind(CssTokenKind.String)) value = ((PreservedToken)reader._values[reader._p++]).Token.Value;
                if (value == null) return null;
                reader.Space();
                var flag = reader.Ident();
                var sensitivity = CaseSensitivity.Default;
                if (flag != null)
                {
                    if (Dom.Names.AsciiEqual(flag, "i")) sensitivity = CaseSensitivity.Insensitive;
                    else if (Dom.Names.AsciiEqual(flag, "s")) sensitivity = CaseSensitivity.Sensitive;
                    else return null;
                    reader.Space();
                }
                return reader.AtEnd ? new AttributeSelector(name, prefix, op, value, sensitivity) { NamespaceURI = reader.ResolveNamespace(prefix, true) } : null;
            }
        }
    }
}
