using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    public sealed partial class CSSStyleDeclaration
    {
        /// <summary>Replaces a computed view. Distinct names (every computed style) are appended and indexed by name;
        /// a repeated name keeps the ordinary declaration-store semantics and leaves the view unindexed.</summary>
        internal void ReplaceComputed(IEnumerable<CssDeclaration> declarations)
        {
            // The cascade's sorted projection normally keeps exactly the same names. Preserve
            // unchanged declaration objects and serialized scalars; structural changes use the
            // ordinary replacement path below (including duplicate-name store semantics).
            if (_computedIndex != null && declarations is IReadOnlyList<CssDeclaration> ordered
                && ordered.Count == _declarations.Count)
            {
                bool sameNames = true;
                for (int i = 0; i < ordered.Count; i++)
                    if (ordered[i].Name != _declarations[i].Name) { sameNames = false; break; }
                if (sameNames)
                {
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (ReferenceEquals(ordered[i], _declarations[i])) continue;
                        _declarations[i] = ordered[i];
                        _computedText[i] = null;
                    }
                    return;
                }
            }
            var index = _computedIndex ?? new Dictionary<string, int>(StringComparer.Ordinal);
            index.Clear();
            Array.Clear(_computedText);
            _computedIndex = null;
            _declarations.Clear();
            bool distinct = true;
            foreach (var declaration in declarations)
            {
                if (distinct && index.TryAdd(declaration.Name, _declarations.Count)) { _declarations.Add(declaration); continue; }
                distinct = false;
                Store(declaration, false);
            }
            if (distinct)
            {
                _computedIndex = index;
                if (_computedText.Length < _declarations.Count) Array.Resize(ref _computedText, _declarations.Count);
            }
        }
        /// <summary>Retain an unchanged immutable computed value, never an equal but separately mutable Typed OM graph.</summary>
        internal CssDeclaration ReuseComputedDeclaration(int ordinal, string name, CssValue value)
        {
            int index = ordinal;
            if (_computedIndex != null &&
                ((uint)ordinal < (uint)_declarations.Count && _declarations[ordinal].Name == name ||
                    _computedIndex.TryGetValue(name, out index)))
            {
                var previous = _declarations[index];
                if (!previous.Important && SameImmutableComputedValue(previous.Value, value))
                    return previous;
            }
            return new CssDeclaration(name, value, false);
        }
        private static bool SameImmutableComputedValue(CssValue first, CssValue second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (!CacheableComputedText(first) || first.GetType() != second.GetType()) return false;
            return first switch
            {
                CssKeywordValue keyword => keyword.Value == ((CssKeywordValue)second).Value,
                CssStringValue text => text.Value == ((CssStringValue)second).Value,
                CssNumberValue number => SameComputedNumber(number.Value, ((CssNumberValue)second).Value)
                    && number.IsInteger == ((CssNumberValue)second).IsInteger,
                CssDimensionValue dimension => SameComputedNumber(dimension.Value, ((CssDimensionValue)second).Value)
                    && dimension.Unit == ((CssDimensionValue)second).Unit,
                CssPercentageValue percent => SameComputedNumber(percent.Value, ((CssPercentageValue)second).Value),
                CssUrlValue url => url.Url == ((CssUrlValue)second).Url && url.CrossOrigin == ((CssUrlValue)second).CrossOrigin
                    && url.Integrity == ((CssUrlValue)second).Integrity && url.ReferrerPolicy == ((CssUrlValue)second).ReferrerPolicy,
                Values.Colors.CssAbsoluteColorValue color => SameComputedColor(color.Color, ((Values.Colors.CssAbsoluteColorValue)second).Color)
                    && color.Keyword == ((Values.Colors.CssAbsoluteColorValue)second).Keyword
                    && color.Legacy == ((Values.Colors.CssAbsoluteColorValue)second).Legacy
                    && ReferenceEquals(color.SpecifiedFunction, ((Values.Colors.CssAbsoluteColorValue)second).SpecifiedFunction),
                _ => false
            };
        }
        private static bool SameComputedNumber(double first, double second) =>
            BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);
        private static bool SameComputedColor(Values.Colors.CssResolvedColor first, Values.Colors.CssResolvedColor second) =>
            first.Space == second.Space && SameComputedNumber(first.C1, second.C1) && SameComputedNumber(first.C2, second.C2)
            && SameComputedNumber(first.C3, second.C3) && SameComputedNumber(first.Alpha, second.Alpha);
        /// <summary>Updates an existing indexed computed longhand without discarding unrelated scalar serialization.</summary>
        internal void ReplaceComputedValue(string name, CssValue value)
        {
            if (_computedIndex == null || !_computedIndex.TryGetValue(name, out int index))
                throw new InvalidOperationException("Missing indexed computed longhand: " + name);
            _declarations[index] = new CssDeclaration(name, value, false);
            _computedText[index] = null;
        }
    }
}
