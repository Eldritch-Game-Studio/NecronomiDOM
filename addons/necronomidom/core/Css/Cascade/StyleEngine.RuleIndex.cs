using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>
    /// Rule hashing by the rightmost compound selector, after WebKit's RuleSet (separate id, class, attribute,
    /// tag and universal rule maps, Source/WebCore/style/RuleSet.cpp) and Stylo's SelectorMap
    /// (servo/components/style/selector_map.rs, bucket preference id &gt; class &gt; attribute &gt; local name &gt;
    /// universal, ties to the rightmost simple selector). Only the rightmost compound must match the element itself,
    /// so each leaf style rule of a rule list is filed under one id, class, attribute name or tag of every complex
    /// selector; an element visits the rules filed under its own id, classes, attribute names and tag, plus the
    /// unkeyed and structural rules, in list order.
    /// Keys compare case-insensitively, a superset of the matcher's quirks-mode and HTML name rules, so a bucket can only
    /// add candidates. Every skipped rule is a leaf style rule whose selector test must fail; the walk still advances
    /// source order over it, so order numbers, layer registration and every cascade candidate equal the linear walk.
    /// </summary>
    public sealed partial class StyleEngine
    {
        private bool _linearRuleWalk;
        /// <summary>Diagnostic linear-walk reference; never changes cascade results.</summary>
        internal bool IndexedRuleMatching { get => !_linearRuleWalk; init => _linearRuleWalk = !value; }
        /// <summary>Diagnostic: every rule collection also runs the linear walk and throws on any difference.</summary>
        internal static bool CrossCheckRuleIndex { get; set; }
        /// <summary>Style rules whose selectors were tested against an element (diagnostics and performance tests).</summary>
        internal long RuleTestCount { get; private set; }
        private readonly ConditionalWeakTable<CSSRuleList, RuleIndex> _ruleIndexes = new();
        private readonly List<int[]> _ruleBuckets = new();

        private readonly struct RuleKey : IEquatable<RuleKey>
        {
            private readonly string _text;
            private readonly int _start, _length;
            internal RuleKey(string text, int start, int length) { _text = text; _start = start; _length = length; }
            internal RuleKey(string text) : this(text, 0, text.Length) { }
            private ReadOnlySpan<char> Span => _text.AsSpan(_start, _length);
            public bool Equals(RuleKey other) => Span.Equals(other.Span, StringComparison.OrdinalIgnoreCase);
            public override bool Equals(object? obj) => obj is RuleKey other && Equals(other);
            public override int GetHashCode() => string.GetHashCode(Span, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class RuleIndex
        {
            private enum Bucket : byte { None, Tag, Attribute, Class, Id }
            // Snapshot that proves the index current after any stylesheet mutation.
            internal readonly CSSRule[] Rules;
            private readonly SelectorList?[] _selectors;
            private readonly bool[] _leaf;
            private readonly int[] _always;
            private readonly Dictionary<RuleKey, int[]>? _ids, _classes, _attributes, _tags;
            internal long Epoch;

            internal RuleIndex(CSSRuleList rules, long epoch)
            {
                Epoch = epoch;
                int count = rules.Count;
                Rules = new CSSRule[count];
                _selectors = new SelectorList?[count];
                _leaf = new bool[count];
                var always = new List<int>();
                Dictionary<RuleKey, List<int>>? ids = null, classes = null, attributes = null, tags = null;
                for (int i = 0; i < count; i++)
                {
                    CSSRule rule = Rules[i] = rules[i];
                    switch (rule)
                    {
                        case CSSStyleRule style:
                            _selectors[i] = style.Selectors;
                            _leaf[i] = style.CssRules.Count == 0;
                            if (!_leaf[i] || !AllKeyed(style.Selectors)) { always.Add(i); break; }
                            foreach (var complex in style.Selectors.Selectors)
                            {
                                Bucket bucket = RightmostKey(complex, out string key);
                                if (bucket == Bucket.Id) File(ref ids, key, i);
                                else if (bucket == Bucket.Class) File(ref classes, key, i);
                                else if (bucket == Bucket.Attribute) File(ref attributes, key, i);
                                else File(ref tags, key, i);
                            }
                            break;
                        case CSSLayerStatementRule or CSSLayerBlockRule or CSSImportRule or CSSMediaRule or CSSSupportsRule
                            or CSSContainerRule or CSSScopeRule or CSSPropertyRule:
                            always.Add(i);
                            break;
                        // Nested declarations outside a nesting parent and every other rule only advance source order.
                    }
                }
                _always = always.ToArray();
                _ids = Freeze(ids); _classes = Freeze(classes); _attributes = Freeze(attributes); _tags = Freeze(tags);
            }
            private static void File(ref Dictionary<RuleKey, List<int>>? map, string key, int position)
            {
                map ??= new Dictionary<RuleKey, List<int>>();
                if (!map.TryGetValue(new RuleKey(key), out var positions)) map.Add(new RuleKey(key), positions = new List<int>());
                // A selector list keyed twice under one name, e.g. "p.a, div.a", files its rule once.
                if (positions.Count == 0 || positions[^1] != position) positions.Add(position);
            }
            private static Dictionary<RuleKey, int[]>? Freeze(Dictionary<RuleKey, List<int>>? map)
            {
                if (map == null) return null;
                var frozen = new Dictionary<RuleKey, int[]>(map.Count);
                foreach (var pair in map) frozen.Add(pair.Key, pair.Value.ToArray());
                return frozen;
            }
            private static bool AllKeyed(SelectorList list)
            {
                foreach (var complex in list.Selectors) if (RightmostKey(complex, out _) == Bucket.None) return false;
                return true;
            }
            /// <summary>Most selective type, id, class or attribute name of the subject compound.</summary>
            private static Bucket RightmostKey(ComplexSelector complex, out string key)
            {
                key = "";
                if (complex.Compounds.Count == 0) return Bucket.None;
                Bucket best = Bucket.None;
                foreach (var simple in complex.Compounds[^1].SimpleSelectors)
                {
                    // Simple selectors around ::part() and ::slotted() test the shadow host or slot, not the element.
                    if (simple is PseudoElementSelector { Name: "part" or "slotted" }) return Bucket.None;
                    (Bucket bucket, string name) = simple switch
                    {
                        IdSelector id => (Bucket.Id, id.Id),
                        ClassSelector cls => (Bucket.Class, cls.ClassName),
                        AttributeSelector attribute => (Bucket.Attribute, attribute.Name),
                        TypeSelector type => (Bucket.Tag, type.Name),
                        _ => (Bucket.None, ""),
                    };
                    if (bucket != Bucket.None && bucket >= best) { best = bucket; key = name; }
                }
                return best;
            }

            /// <summary>Same rules and selector lists as when indexed; declaration edits keep an index valid.</summary>
            internal bool Matches(CSSRuleList rules)
            {
                if (rules.Count != Rules.Length) return false;
                for (int i = 0; i < Rules.Length; i++)
                {
                    if (!ReferenceEquals(rules[i], Rules[i])) return false;
                    if (Rules[i] is CSSStyleRule style
                        && (!ReferenceEquals(style.Selectors, _selectors[i]) || (style.CssRules.Count == 0) != _leaf[i])) return false;
                }
                return true;
            }

            /// <summary>Ascending positions of the rules the element must visit; a rented array is returned by the caller.</summary>
            internal ReadOnlySpan<int> Candidates(Element element, List<int[]> buckets, ref int[]? rented)
            {
                buckets.Clear();
                int total = _always.Length;
                if (_always.Length > 0) buckets.Add(_always);
                if (_ids != null && element.GetAttribute("id") is { Length: > 0 } id) total += Add(_ids, new RuleKey(id), buckets);
                if (_classes != null && element.GetAttribute("class") is { Length: > 0 } classes)
                {
                    int start = 0;
                    while (start < classes.Length)
                    {
                        while (start < classes.Length && DOMTokenList.IsSpace(classes[start])) start++;
                        int end = start;
                        while (end < classes.Length && !DOMTokenList.IsSpace(classes[end])) end++;
                        if (end > start) total += Add(_classes, new RuleKey(classes, start, end - start), buckets);
                        start = end;
                    }
                }
                if (_attributes != null)
                    foreach (Attr attribute in element.AttributeList) total += Add(_attributes, new RuleKey(attribute.LocalName), buckets);
                if (_tags != null) total += Add(_tags, new RuleKey(element.LocalName), buckets);
                if (buckets.Count == 0) return default;
                if (buckets.Count == 1) return buckets[0];
                rented = ArrayPool<int>.Shared.Rent(total);
                int length = 0;
                foreach (int[] bucket in buckets) { bucket.CopyTo(rented, length); length += bucket.Length; }
                Span<int> merged = rented.AsSpan(0, length);
                merged.Sort();
                int unique = 0;
                for (int i = 0; i < merged.Length; i++)
                    if (unique == 0 || merged[i] != merged[unique - 1]) merged[unique++] = merged[i];
                return merged[..unique];
            }
            private static int Add(Dictionary<RuleKey, int[]> map, RuleKey key, List<int[]> buckets)
            {
                if (!map.TryGetValue(key, out int[]? positions)) return 0;
                buckets.Add(positions);
                return positions.Length;
            }
        }

        private RuleIndex RuleIndexFor(CSSRuleList rules)
        {
            long epoch = CSSStyleSheet.MutationEpoch;
            if (_ruleIndexes.TryGetValue(rules, out var index))
            {
                if (index.Epoch == epoch) return index;
                if (index.Matches(rules)) { index.Epoch = epoch; return index; }
            }
            index = new RuleIndex(rules, epoch);
            _ruleIndexes.AddOrUpdate(rules, index);
            return index;
        }

        /// <summary>Visits the indexed candidates of a top-level rule list, advancing source order over skipped leaf rules.</summary>
        private void WalkIndexedRules(CSSRuleList rules, SheetSource source, Entry entry, RuleCollection result,
            CascadeLayers layers, CascadeLayers.Layer layer, CascadeOrigin origin, SelectorMatchContext context,
            int proximity, MediaEnvironment environment)
        {
            RuleIndex index = RuleIndexFor(rules);
            int[]? rented = null;
            try
            {
                ReadOnlySpan<int> candidates = index.Candidates(entry.Element, _ruleBuckets, ref rented);
                int next = 0;
                foreach (int position in candidates)
                {
                    result.Order += position - next;
                    next = position + 1;
                    WalkRule(index.Rules[position], source, entry, result, layers, layer, origin, context, null, proximity, environment);
                }
                result.Order += index.Rules.Length - next;
            }
            finally
            {
                if (rented != null) ArrayPool<int>.Shared.Return(rented);
            }
        }

        private void CrossCheck(Entry entry, RuleCollection indexed)
        {
            var linear = new RuleCollection();
            long tests = RuleTestCount;
            _linearRuleWalk = true;
            try { Gather(entry, linear); }
            finally { _linearRuleWalk = false; RuleTestCount = tests; }
            bool same = linear.Order == indexed.Order && linear.Candidates.Count == indexed.Candidates.Count
                && linear.Properties.Count == indexed.Properties.Count;
            for (int i = 0; same && i < linear.Candidates.Count; i++)
            {
                Candidate a = linear.Candidates[i], b = indexed.Candidates[i];
                // Declared values are shared with the stylesheet; presentational hints are allocated per collection.
                bool sameValue = ReferenceEquals(a.Value, b.Value) || a.Priority.Order == long.MinValue
                    && a.Value.GetType() == b.Value.GetType() && a.Value.Serialize() == b.Value.Serialize();
                same = a.Name == b.Name && sameValue && a.Priority == b.Priority
                    && a.Layer.Order == b.Layer.Order && ReferenceEquals(a.Root, b.Root);
            }
            foreach (var pair in linear.Properties)
                same &= indexed.Properties.TryGetValue(pair.Key, out var registration) && registration == pair.Value;
            if (!same)
                throw new InvalidOperationException("Rule index diverged from the linear walk for <" + entry.Element.LocalName + ">"
                    + (entry.Pseudo == null ? "" : "::" + entry.Pseudo) + ".");
        }
    }
}
