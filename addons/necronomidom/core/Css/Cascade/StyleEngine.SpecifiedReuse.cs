using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade;

public sealed partial class StyleEngine
{
    private sealed class SpecifiedIndex
    {
        internal readonly Dictionary<string, List<int>>[] Physical = [new(StringComparer.Ordinal), new(StringComparer.Ordinal)];
        internal readonly List<int>[] Inherited = [new(), new()];
        internal SpecifiedIndex()
        {
            var catalog = ComputedLonghands.Value;
            for (int direction = 0; direction < 2; direction++)
                for (int i = 0; i < catalog.Length; i++)
                {
                    var property = direction == 0 ? catalog[i].Ltr : catalog[i].Rtl;
                    if (!Physical[direction].TryGetValue(property.Name, out var indices))
                        Physical[direction].Add(property.Name, indices = new());
                    indices.Add(i);
                    if (property.Inherited) Inherited[direction].Add(i);
                }
        }
    }
    private static readonly Lazy<SpecifiedIndex> SpecifiedInputs = new(() => new());

    private sealed partial class ComputedReuse
    {
        internal readonly List<int> SpecifiedExplicit = new();
        internal readonly bool[] SpecifiedSelected = new bool[ComputedLonghands.Value.Length];
        private bool _specifiedReady, _specifiedRtl;
        internal bool ReuseSpecified(bool rtl)
        {
            bool reuse = _rulesStable && _previousSnapshot && _specifiedReady && _specifiedRtl == rtl;
            _specifiedReady = false;
            _specifiedRtl = rtl;
            if (!reuse) return false;
            Array.Clear(SpecifiedSelected);
            return true;
        }
        internal void CaptureSpecified(Dictionary<string, List<Candidate>> groups, bool rtl)
        {
            SpecifiedExplicit.Clear();
            var index = SpecifiedInputs.Value.Physical[rtl ? 1 : 0];
            foreach (var pair in groups)
                if (index.TryGetValue(pair.Key, out var indices)) SpecifiedExplicit.AddRange(indices);
            _specifiedReady = true;
        }
    }

    private void ComputeSpecifiedInputs(Entry entry, Entry? parent, Dictionary<string, List<Candidate>> groups,
        CustomPropertyResolver variables, bool rtl, ref Dictionary<(CssPendingSubstitutionValue, string), ParsedProperty?>? substituted)
    {
        var cache = entry.ComputedReuse;
        if (cache?.ReuseSpecified(rtl) == true)
        {
            // Removed explicit declarations must be defaulted again as well as current declarations.
            SelectSpecifiedIndices(cache.SpecifiedExplicit, entry, parent, groups, variables, rtl, ref substituted);
            var index = SpecifiedInputs.Value;
            SelectSpecifiedIndices(index.Inherited[rtl ? 1 : 0], entry, parent, groups, variables, rtl, ref substituted);
            foreach (var pair in groups)
                if (index.Physical[rtl ? 1 : 0].TryGetValue(pair.Key, out var indices))
                    SelectSpecifiedIndices(indices, entry, parent, groups, variables, rtl, ref substituted);
        }
        else
        {
            entry.Specified.Clear();
            foreach (ref readonly var longhand in ComputedLonghands.Value.AsSpan())
            {
                ref readonly var property = ref (rtl ? ref longhand.Rtl : ref longhand.Ltr);
                ComputeSpecifiedLonghand(entry, parent, longhand.Definition, property, groups, variables, rtl, ref substituted);
            }
        }
        cache?.CaptureSpecified(groups, rtl);
    }
    private void SelectSpecifiedIndices(List<int> indices, Entry entry, Entry? parent,
        Dictionary<string, List<Candidate>> groups, CustomPropertyResolver variables, bool rtl,
        ref Dictionary<(CssPendingSubstitutionValue, string), ParsedProperty?>? substituted)
    {
        var selected = entry.ComputedReuse!.SpecifiedSelected;
        var catalog = ComputedLonghands.Value;
        foreach (int i in indices)
        {
            if (selected[i]) continue;
            selected[i] = true;
            ref readonly var longhand = ref catalog[i];
            ref readonly var property = ref (rtl ? ref longhand.Rtl : ref longhand.Ltr);
            ComputeSpecifiedLonghand(entry, parent, longhand.Definition, property, groups, variables, rtl, ref substituted);
        }
    }
    private void ComputeSpecifiedLonghand(Entry entry, Entry? parent, PropertyDefinition definition,
        in LonghandInput property, Dictionary<string, List<Candidate>> groups, CustomPropertyResolver variables, bool rtl,
        ref Dictionary<(CssPendingSubstitutionValue, string), ParsedProperty?>? substitutedDeclarations)
    {
        CssValue value = Specified(property, groups, parent, out CssValue? cascaded);
        bool inheritedColor = definition.Name == "color" && (cascaded is null or CssWideKeyword { Kind: CssWideKeywordKind.Inherit or CssWideKeywordKind.Unset });
        if (value is CssPendingSubstitutionValue pending)
        {
            string parsedName = pending.Shorthand ?? definition.Name;
            if (substitutedDeclarations == null)
            {
                substitutedDeclarations = new();
                if (IncrementalReconciliation) entry.SubstitutedDeclarations = substitutedDeclarations;
            }
            if (!substitutedDeclarations.TryGetValue((pending, parsedName), out ParsedProperty? parsed))
            {
                var tokens = variables.Substitute(pending.Tokens, out bool tainted);
                parsed = tokens == null || tainted && ContainsUrl(tokens) ? null : ParseSubstituted(parsedName, tokens);
                substitutedDeclarations.Add((pending, parsedName), parsed);
            }
            CssValue? substitutedValue = ParsedLonghand(parsed, property.Name, rtl);
            value = substitutedValue ?? Unset(definition.Name, parent);
            if (definition.Name == "color" && substitutedValue == null) inheritedColor = true;
        }
        if (GeometryProperty(definition.Name) && (cascaded is CssWideKeyword { Kind: CssWideKeywordKind.Inherit } ||
            value is CssWideKeyword { Kind: CssWideKeywordKind.Inherit })) entry.ReadsParentGeometry = true;
        entry.Specified[definition.Name] = value;
        if (definition.Name == "color")
        {
            entry.ForegroundInherited = inheritedColor;
            entry.ForegroundExpression = !inheritedColor && UsesCurrentColor(value) ? value : null;
            entry.ForegroundParent = inheritedColor || entry.ForegroundExpression != null ? parent?.Element : null;
        }
    }
}
