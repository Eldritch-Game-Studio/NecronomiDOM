using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>Resolved background paint and the existing typed CSS transition lists.</summary>
public readonly record struct BackgroundColorTransitionStyle(Rgba Color, bool Enabled, double Duration,
    double Delay, CssCubicEasing Easing)
{
    internal string? TimingKeyword { get; init; }
    public static BackgroundColorTransitionStyle Resolve(CSSStyleDeclaration computed)
    {
        if (computed.GetSpecifiedValue("background-color") is not ComputedValueColor color)
            throw new NotSupportedException("Runtime requires resolved background-color.");
        var rgb = color.Color.ToSrgb();
        var timing = TransitionTimingStyle.Resolve(computed, "background-color");
        return new(new Rgba((float)rgb.C1, (float)rgb.C2, (float)rgb.C3, (float)rgb.Alpha),
            timing.Enabled, timing.Duration, timing.Delay, timing.Easing)
        { TimingKeyword = (timing.TimingFunction as CssKeywordValue)?.Value };
    }
}
