using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Layout;

/// <summary>Computed target identity, before CSSOM resolves percentages against a layout box.</summary>
public readonly record struct WidthTransitionStyle(AnimatedWidth? Width, bool Enabled, double Duration,
    double Delay, CssCubicEasing Easing)
{
    internal bool IsPercent { get; init; }
    internal string? TimingKeyword { get; init; }
    public static WidthTransitionStyle Resolve(CSSStyleDeclaration computedValues)
    {
        var timing = TransitionTimingStyle.Resolve(computedValues, "width");
        CssValue value = computedValues.GetSpecifiedValue("width")
            ?? throw new NotSupportedException("Missing computed width.");
        AnimatedWidth? width = value switch
        {
            CssDimensionValue { Unit: "px" } px => new(px.Value, 0),
            CssPercentageValue percent => new(0, percent.Value, true),
            CssNumberValue { Value: 0 } => new(0, 0),
            CssKeywordValue { Value: "auto" } => null,
            _ => throw new NotSupportedException("Width transitions require computed pixels, percentages or auto; received " + value.Serialize())
        };
        if (width is { } target && (!double.IsFinite(target.Pixels) || !double.IsFinite(target.Percent)))
            throw new NotSupportedException("Nonfinite width transition target.");
        return new(width, timing.Enabled, timing.Duration, timing.Delay, timing.Easing)
        { IsPercent = value is CssPercentageValue, TimingKeyword = (timing.TimingFunction as CssKeywordValue)?.Value };
    }
}
