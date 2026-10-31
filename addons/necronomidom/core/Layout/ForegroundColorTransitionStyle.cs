using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>Computed foreground ink and the existing typed CSS transition lists.</summary>
public readonly record struct ForegroundColorTransitionStyle(Rgba Color, bool Enabled, double Duration,
    double Delay, CssCubicEasing Easing)
{
    internal string? TimingKeyword { get; init; }
    public static ForegroundColorTransitionStyle Resolve(CSSStyleDeclaration computed)
    {
        if (computed.GetSpecifiedValue("color") is not ComputedValueColor color)
            throw new NotSupportedException("Runtime requires resolved foreground color.");
        var rgb = color.Color.ToSrgb();
        var timing = TransitionTimingStyle.Resolve(computed, "color");
        return new(new Rgba((float)rgb.C1, (float)rgb.C2, (float)rgb.C3, (float)rgb.Alpha),
            timing.Enabled, timing.Duration, timing.Delay, timing.Easing)
        { TimingKeyword = (timing.TimingFunction as CssKeywordValue)?.Value };
    }

    internal static Rgba PaintColor(in ForegroundColorDependencies dependencies, Rgba parent)
    {
        if (dependencies.Inherited && dependencies.Parent != null) return parent;
        if (dependencies.Expression is CssAbsoluteColorValue { Keyword: "currentcolor" } && dependencies.Parent != null) return parent;
        if (dependencies.Expression is CssColorValue expression && dependencies.Parent != null)
        {
            if (!expression.TryResolve(new CssColorContext
                { CurrentColor = new("srgb", parent.R, parent.G, parent.B, parent.A) }, out var resolved))
                throw new NotSupportedException("Runtime cannot resolve sampled foreground currentcolor.");
            var rgb = resolved.ToSrgb();
            return new((float)rgb.C1, (float)rgb.C2, (float)rgb.C3, (float)rgb.Alpha);
        }
        var target = dependencies.Target.Color.ToSrgb();
        return new((float)target.C1, (float)target.C2, (float)target.C3, (float)target.Alpha);
    }
}
