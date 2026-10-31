using System;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Url;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Values 4 § 6.1 font-relative bases, in CSS pixels; null denotes unavailable metrics.</summary>
    public readonly record struct ComputedFontMetrics(double? Size = null, double? Ex = null,
        double? Ch = null, double? Cap = null, double? Ic = null, double? LineHeight = null, double? IcHeight = null);

    /// <summary>CSS Values 4 § 6.1.2 viewport bases in CSS pixels, independent of layout boxes.</summary>
    public readonly record struct ComputedViewportMetrics(double? Width = null, double? Height = null);

    /// <summary>Immutable CSS computed-value inputs (Values 4 §§ 6, 10; Color 4 § 14). Missing bases stay unresolved.</summary>
    public readonly record struct ComputedValueContext
    {
        public ComputedFontMetrics Font { get; init; }
        public ComputedFontMetrics ParentFont { get; init; }
        public ComputedFontMetrics RootFont { get; init; }
        public ComputedFontMetrics InitialFont { get; init; }
        public ComputedViewportMetrics Viewport { get; init; }
        public ComputedViewportMetrics SmallViewport { get; init; }
        public ComputedViewportMetrics LargeViewport { get; init; }
        public ComputedViewportMetrics DynamicViewport { get; init; }
        /// <summary>Returns the eligible container axis size (100 units), or null when not yet known.</summary>
        public Func<string, double?>? ContainerUnitBasis { get; init; }
        /// <summary>True only when the provider knows no eligible container exists; enables the specified small-viewport fallback.</summary>
        public bool NoEligibleContainer { get; init; }
        public bool IsRoot { get; init; }
        public bool DarkColorScheme { get; init; }
        public bool IsFlexOrGridItem { get; init; }
        public double? ParentFontWeight { get; init; }
        public double? MediumFontSize { get; init; }
        public double? DevicePixelRatio { get; init; }
        public CssValue? CurrentColor { get; init; }
        public Func<string, string, CssValue?>? SystemFont { get; init; }
        public Func<CssValue, double?>? NumericValue { get; init; }
        /// <summary>CSS Fonts 4 math-size scale from parent to current math depth, supplied by the font/MATH-table provider.</summary>
        public double? MathFontSizeScale { get; init; }
        /// <summary>Font-palette resource computation/equivalence; unavailable font palettes remain symbolic.</summary>
        public Func<CssValue, CssValue?>? FontPaletteValue { get; init; }
        public CssValue? InheritedColor { get; init; }
        public Func<string, CssResolvedColor?>? SystemColor { get; init; }
        /// <summary>Returns sibling specified/computed properties needed for cross-property rules, without recursive resolution.</summary>
        public Func<string, CssValue?>? PropertyValue { get; init; }
        public Func<string, CssValue?>? ParentPropertyValue { get; init; }
        public WebUrl? DocumentUrl { get; init; }
        internal Action? ViewportUnitRead { get; init; }
    }
}
