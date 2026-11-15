using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    /// <summary>The immutable name set of a computed view: names in view order and their ordinals. Elements that expose
    /// the same longhands and custom properties share one layout, so each view stores only its values.</summary>
    internal sealed class ComputedStyleLayout
    {
        internal readonly string[] Names;
        internal readonly Dictionary<string, int> Index;
        // Names in the enumeration order of the values the layout was built from, with their ordinals. Elements
        // normally produce their values in that same order, so a fill matches each name by reference, not by hashing.
        private readonly string[]? _orderNames;
        private readonly int[]? _orderOrdinals;
        private ComputedStyleLayout(string[] names, Dictionary<string, int> index, string[]? orderNames = null)
        {
            Names = names; Index = index;
            if (orderNames == null) return;
            _orderNames = orderNames;
            _orderOrdinals = new int[orderNames.Length];
            for (int i = 0; i < orderNames.Length; i++) _orderOrdinals[i] = index[orderNames[i]];
        }
        internal int Count => Names.Length;
        /// <summary>Null when a name repeats; a computed view then keeps the ordinary declaration-store semantics.</summary>
        internal static ComputedStyleLayout? TryCreate(string[] names)
        {
            var index = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
                if (!index.TryAdd(names[i], i)) return null;
            return new ComputedStyleLayout(names, index);
        }
        /// <summary>The keys of <paramref name="values"/> in ordinal order (CSSOM orders a computed block's longhands).</summary>
        internal static ComputedStyleLayout FromValues(Dictionary<string, CssValue> values)
        {
            var order = new string[values.Count];
            values.Keys.CopyTo(order, 0);
            var names = (string[])order.Clone();
            Array.Sort(names, StringComparer.Ordinal);
            var index = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++) index.Add(names[i], i);
            return new ComputedStyleLayout(names, index, order);
        }
        /// <summary>The ordinal of <paramref name="name"/>, met at <paramref name="position"/> while enumerating values.</summary>
        internal bool TryGetOrdinal(int position, string name, out int ordinal)
        {
            if (_orderNames != null && (uint)position < (uint)_orderNames.Length && ReferenceEquals(_orderNames[position], name))
            {
                ordinal = _orderOrdinals![position];
                return true;
            }
            return Index.TryGetValue(name, out ordinal);
        }
        /// <summary>Exactly the keys of <paramref name="values"/>, in any order.</summary>
        internal bool HasNames(Dictionary<string, CssValue> values)
        {
            if (values.Count != Names.Length) return false;
            int position = 0;
            foreach (string name in values.Keys)
                if (!TryGetOrdinal(position++, name, out _)) return false;
            return true;
        }
        internal bool HasNames(string[] names)
        {
            if (names.Length != Names.Length) return false;
            for (int i = 0; i < names.Length; i++)
                if (names[i] != Names[i]) return false;
            return true;
        }
    }

    public sealed partial class CSSStyleDeclaration
    {
        // A computed view keeps values in the slots of a shared layout. Name reads go straight to a slot, and
        // _declarations only mirrors the slots for record reads, rebuilt the first time a record is read after a change.
        private ComputedStyleLayout? _layout;
        private CssValue[] _layoutValues = Array.Empty<CssValue>();
        private int _recordsVersion = -1;
        internal ComputedStyleLayout? ComputedLayout => _layout;

        /// <summary>Replaces a computed view. Distinct names keep their given order and become an indexed view;
        /// a repeated name or an important record keeps the ordinary declaration-store semantics, unindexed.</summary>
        internal void ReplaceComputed(IEnumerable<CssDeclaration> declarations)
        {
            var replacement = new List<CssDeclaration>(declarations);
            var names = new string[replacement.Count];
            bool important = false;
            for (int i = 0; i < names.Length; i++) { names[i] = replacement[i].Name; important |= replacement[i].Important; }
            ComputedStyleLayout? layout = important ? null : _layout != null && _layout.HasNames(names) ? _layout : ComputedStyleLayout.TryCreate(names);
            if (layout == null)
            {
                DropComputedLayout();
                _version++;
                _declarations.Clear();
                foreach (var declaration in replacement) Store(declaration, false);
                return;
            }
            BeginComputedLayout(layout);
            for (int i = 0; i < names.Length; i++) SetComputedValue(i, replacement[i].Value);
        }
        /// <summary>Starts updating a computed view over <paramref name="layout"/>. The current layout keeps every slot
        /// for comparison; another layout starts empty, so the caller must then set every slot.</summary>
        internal void BeginComputedLayout(ComputedStyleLayout layout)
        {
            if (ReferenceEquals(_layout, layout)) return;
            int count = layout.Count;
            if (_layoutValues.Length < count) _layoutValues = new CssValue[count]; else Array.Clear(_layoutValues);
            if (_computedText.Length < count) _computedText = new object?[count]; else Array.Clear(_computedText);
            _layout = layout;
            _declarations.Clear();
            _version++;
        }
        /// <summary>Retains an unchanged immutable computed value with its serialized text, never an equal but separately
        /// mutable Typed OM graph; <see cref="Version"/> advances only for a changed slot.</summary>
        internal void SetComputedValue(int ordinal, CssValue value)
        {
            CssValue? previous = _layoutValues[ordinal];
            if (previous != null && SameImmutableComputedValue(previous, value)) return;
            _layoutValues[ordinal] = value;
            _computedText[ordinal] = null;
            _version++;
        }
        /// <summary>Updates an existing computed longhand without discarding unrelated scalar serialization.</summary>
        internal void ReplaceComputedValue(string name, CssValue value)
        {
            if (_layout == null || !_layout.Index.TryGetValue(name, out int index))
                throw new InvalidOperationException("Missing indexed computed longhand: " + name);
            _layoutValues[index] = value;
            _computedText[index] = null;
            _version++;
        }
        /// <summary>The declaration records, materialized from a computed view's slots only when a record is read.</summary>
        private List<CssDeclaration> Records()
        {
            if (_layout == null || _recordsVersion == _version) return _declarations;
            string[] names = _layout.Names;
            if (_declarations.Count != names.Length) _declarations.Clear();
            for (int i = 0; i < names.Length; i++)
            {
                if (i == _declarations.Count) { _declarations.Add(new CssDeclaration(names[i], _layoutValues[i], false)); continue; }
                CssDeclaration record = _declarations[i];
                if (!ReferenceEquals(record.Name, names[i]) || !ReferenceEquals(record.Value, _layoutValues[i]))
                    _declarations[i] = new CssDeclaration(names[i], _layoutValues[i], false);
            }
            _recordsVersion = _version;
            return _declarations;
        }
        /// <summary>An ordinary mutation continues from the computed view's records.</summary>
        private void DropComputedLayout()
        {
            if (_layout == null) return;
            Records();
            _layout = null;
            _layoutValues = Array.Empty<CssValue>();
            _computedText = Array.Empty<object?>();
        }
        private bool TryFind(string name, out CssValue value, out bool important)
        {
            important = false;
            if (_layout != null)
            {
                bool found = _layout.Index.TryGetValue(name, out int index);
                value = found ? _layoutValues[index] : null!;
                return found;
            }
            foreach (var declaration in _declarations)
                if (declaration.Name == name) { value = declaration.Value; important = declaration.Important; return true; }
            value = null!;
            return false;
        }
        private CssValue? FindValue(string name) => TryFind(name, out CssValue value, out _) ? value : null;
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
    }
}
