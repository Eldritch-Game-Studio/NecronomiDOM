using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Css.Cascade;

public sealed partial class StyleEngine
{
    // The supported longhand catalog is already process-wide and immutable. Resolve its
    // physical metadata/initials once too, not for every absent declaration on every refresh.
    private readonly record struct LonghandInput(string Name, CssValue Initial, bool Inherited);
    private static LonghandInput LonghandInputFor(string name) =>
        new(name, Initial(name), CssPropertyRegistry.Get(name)?.Inherited == true);
    private static readonly Lazy<(PropertyDefinition Definition, LonghandInput Ltr, LonghandInput Rtl)[]> ComputedLonghands = new(() =>
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var longhands = new List<(PropertyDefinition, LonghandInput, LonghandInput)>();
        foreach (string name in CssPropertyRegistry.SupportedNames)
            if (CssPropertyRegistry.Get(name) is { IsShorthand: false } definition && seen.Add(definition.Name))
            {
                string ltr = PhysicalName(definition.Name, false), rtl = PhysicalName(definition.Name, true);
                var left = LonghandInputFor(ltr);
                longhands.Add((definition, left, rtl == ltr ? left : LonghandInputFor(rtl)));
            }
        return longhands.ToArray();
    });

    internal long ReusedComputedValueCount { get; private set; }

    // Reuse records retain only immutable scalars and their observed callback dependencies.
    private sealed partial class ComputedReuse
    {
        private readonly record struct ReusedValue(string Name, CssValue Specified, CssValue Value, Dependencies? Dependencies);
        private readonly ReusedValue[] _values = new ReusedValue[ComputedLonghands.Value.Length];
        private readonly Dictionary<string, Dependencies> _dependencies = new(StringComparer.Ordinal);
        private string? _property;
        private Dependencies? _active;
        private ComputedValueContext _context;
        private string? _documentHref;
        private bool _hasSnapshot, _previousSnapshot, _rulesStable;
        private CssValue? _stroke, _shadow;

        private sealed class Dependencies
        {
            internal readonly Dictionary<string, CssValue?> Own = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, CssValue?> Parent = new(StringComparer.Ordinal);
            internal bool Dynamic;
            internal void Clear() { Own.Clear(); Parent.Clear(); Dynamic = false; }
            internal bool Match(Entry entry, Entry? parent)
            {
                if (Dynamic) return false;
                foreach (var pair in Own)
                    if (!SameValue(pair.Value, entry.Values.GetValueOrDefault(pair.Key) ?? entry.Specified.GetValueOrDefault(pair.Key))) return false;
                foreach (var pair in Parent)
                    if (!SameValue(pair.Value, parent?.Values.GetValueOrDefault(pair.Key))) return false;
                return true;
            }
            private static bool SameValue(CssValue? before, CssValue? after) => before == null
                ? after == null : ImmutableScalar(before) && ReferenceEquals(before, after);
        }
        internal void Begin(Entry entry, bool rulesStable)
        {
            entry.Values.Clear();
            _stroke = entry.InheritedStrokeColor; _shadow = entry.InheritedTextShadow;
            _previousSnapshot = _hasSnapshot; _hasSnapshot = false;
            _rulesStable = rulesStable; _property = null; _active = null;
        }
        internal bool Admit(in ComputedValueContext context)
        {
            // Never compare closures by identity or retain provider callbacks in the basis fingerprint.
            var scalar = context with { ContainerUnitBasis = null, NumericValue = null,
                PropertyValue = null, ParentPropertyValue = null, SystemFont = null,
                FontPaletteValue = null, SystemColor = null, ViewportUnitRead = null };
            bool reusable = _rulesStable && _previousSnapshot &&
                _documentHref == context.DocumentUrl?.Href && SameContext(_context, scalar);
            _context = scalar; _documentHref = context.DocumentUrl?.Href;
            return reusable;
        }
        internal bool TryValue(Entry entry, Entry? parent, int ordinal, string name, CssValue specified, out CssValue value)
        {
            // A changed iteration order must miss, never return another longhand's result.
            ref readonly var previous = ref _values[ordinal];
            if (previous.Name == name && ReferenceEquals(previous.Specified, specified) &&
                (previous.Dependencies == null || previous.Dependencies.Match(entry, parent)))
            {
                value = previous.Value;
                if (name == "-webkit-text-stroke-color") entry.InheritedStrokeColor = _stroke;
                if (name == "text-shadow") entry.InheritedTextShadow = _shadow;
                return true;
            }
            value = null!;
            return false;
        }
        internal void BeginValue(string name)
        {
            _property = name;
            _dependencies.TryGetValue(name, out _active);
            _active?.Clear();
        }
        internal void EndValue() { _property = null; _active = null; }
        internal void CaptureValue(int ordinal, string name, CssValue specified, CssValue value)
        {
            if (ImmutableScalar(specified) && ImmutableScalar(value))
                _values[ordinal] = new(name, specified, value, _active);
            else _values[ordinal] = default;
        }
        private Dependencies? Active()
        {
            if (_property == null) return null;
            if (_active == null) _dependencies[_property] = _active = new();
            return _active;
        }
        internal void ReadOwn(string name, CssValue? value)
        {
            if (Active() is { } dependencies) dependencies.Own[name] = value;
        }
        internal void ReadParent(string name, CssValue? value)
        {
            if (Active() is { } dependencies) dependencies.Parent[name] = value;
        }
        internal void ReadDynamic()
        {
            if (Active() is { } dependencies) dependencies.Dynamic = true;
        }
        internal void Complete() => _hasSnapshot = true;

        private static bool ImmutableScalar(CssValue value)
        {
            Type type = value.GetType();
            return type == typeof(CssKeywordValue) || type == typeof(CssNumberValue) || type == typeof(CssIntegerValue) ||
                type == typeof(CssDimensionValue) || type == typeof(CssPercentageValue) || type == typeof(CssStringValue) ||
                type == typeof(CssUrlValue) || type == typeof(CssAbsoluteColorValue) || type == typeof(ComputedValueColor);
        }
        // Record equality collapses -0/+0 and NaN payloads; unit bases retain exact numeric bits.
        private static bool Same(double? a, double? b) => a.HasValue == b.HasValue &&
            (!a.HasValue || BitConverter.DoubleToInt64Bits(a.Value) == BitConverter.DoubleToInt64Bits(b!.Value));
        private static bool Same(ComputedFontMetrics a, ComputedFontMetrics b) => Same(a.Size, b.Size) &&
            Same(a.Ex, b.Ex) && Same(a.Ch, b.Ch) && Same(a.Cap, b.Cap) && Same(a.Ic, b.Ic) &&
            Same(a.LineHeight, b.LineHeight) && Same(a.IcHeight, b.IcHeight);
        private static bool Same(ComputedViewportMetrics a, ComputedViewportMetrics b) => Same(a.Width, b.Width) && Same(a.Height, b.Height);
        private static bool SameContext(in ComputedValueContext a, in ComputedValueContext b) => a.Equals(b) &&
            ReferenceEquals(a.CurrentColor, b.CurrentColor) && ReferenceEquals(a.InheritedColor, b.InheritedColor) &&
            Same(a.Font, b.Font) && Same(a.ParentFont, b.ParentFont) && Same(a.RootFont, b.RootFont) && Same(a.InitialFont, b.InitialFont) &&
            Same(a.Viewport, b.Viewport) && Same(a.SmallViewport, b.SmallViewport) && Same(a.LargeViewport, b.LargeViewport) &&
            Same(a.DynamicViewport, b.DynamicViewport) && Same(a.ParentFontWeight, b.ParentFontWeight) &&
            Same(a.MediumFontSize, b.MediumFontSize) && Same(a.DevicePixelRatio, b.DevicePixelRatio) && Same(a.MathFontSizeScale, b.MathFontSizeScale);
    }
    private void BeginComputedRefresh(Entry entry)
    {
        if (IncrementalReconciliation && ValueContext == null && ContainerEnvironment == null)
            (entry.ComputedReuse ??= new()).Begin(entry, entry.ReuseRules && entry.RuleGeneration == _generation);
        else
        {
            entry.ComputedReuse = null;
            entry.Values.Clear();
        }
    }
    private void ComputeRemainingValues(Entry entry, Entry? parent, Dictionary<string, CssValue> specified,
        in ComputedValueContext context)
    {
        bool reuse = entry.ComputedReuse?.Admit(context) == true;
        int ordinal = 0;
        foreach (var pair in specified)
        {
            if (!entry.Values.ContainsKey(pair.Key))
                entry.Values[pair.Key] = ComputeOrReuseValue(entry, parent, ordinal, pair.Key, pair.Value, context, reuse);
            ordinal++;
        }
    }
    private CssValue ComputeOrReuseValue(Entry entry, Entry? parent, int ordinal, string name, CssValue specified,
        in ComputedValueContext context, bool reuse)
    {
        var cache = entry.ComputedReuse;
        if (reuse && cache!.TryValue(entry, parent, ordinal, name, specified, out var value))
        {
            ReusedComputedValueCount++;
            return value;
        }
        cache?.BeginValue(name);
        try
        {
            var resolved = ComputeTextColor(name, specified, entry, context);
            cache?.CaptureValue(ordinal, name, specified, resolved);
            return resolved;
        }
        finally { cache?.EndValue(); }
    }
    private static CssValue? ReadOwnContext(Entry entry, Dictionary<string, CssValue> specified, string name)
    {
        CssValue? value = entry.Values.GetValueOrDefault(name) ?? specified.GetValueOrDefault(name);
        entry.ComputedReuse?.ReadOwn(name, value);
        return value;
    }
    private static CssValue? ReadParentContext(Entry entry, Entry? parent, string name)
    {
        CssValue? value = parent?.Values.GetValueOrDefault(name);
        entry.ComputedReuse?.ReadParent(name, value);
        return value;
    }
    private static double? ReadNumericContext(Entry entry, Func<CssValue, double?>? supplied, CssValue value)
    {
        entry.ComputedReuse?.ReadDynamic();
        return supplied?.Invoke(value) ?? SiblingNumericValue(entry.Element, value);
    }
}
