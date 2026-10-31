using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Layout;

/// <summary>Computed opacity transition parameters; times are seconds, not engine ticks.</summary>
public readonly record struct OpacityTransitionStyle(double Opacity, bool Enabled, double Duration, double Delay, CssCubicEasing Easing)
{
    public static OpacityTransitionStyle Resolve(CSSStyleDeclaration computed)
    {
        CssValue Required(string name) => computed.GetSpecifiedValue(name)
            ?? throw new NotSupportedException("Missing computed transition property: " + name);
        double opacity = Required("opacity") switch
        {
            CssNumberValue number => number.Value,
            CssPercentageValue percentage => percentage.Value / 100,
            _ => throw new NotSupportedException("Runtime requires computed numeric opacity.")
        };
        if (!double.IsFinite(opacity)) throw new NotSupportedException("Nonfinite computed opacity.");
        var timing = TransitionTimingStyle.Resolve(computed, "opacity");
        return new(Math.Clamp(opacity, 0, 1), timing.Enabled, timing.Duration, timing.Delay, timing.Easing);
    }
}

/// <summary>Shared typed property-list matching and repeated timing lists, without reparsing CSS text.</summary>
internal readonly record struct TransitionTimingStyle(bool Enabled, double Duration, double Delay,
    CssCubicEasing Easing, CssValue? TimingFunction)
{
    internal static TransitionTimingStyle Resolve(CSSStyleDeclaration computed, string property)
    {
        CssValue Required(string name) => computed.GetSpecifiedValue(name)
            ?? throw new NotSupportedException("Missing computed transition property: " + name);
        CssValue properties = Required("transition-property");
        int selected = -1;
        for (int i = 0; i < Count(properties); i++)
            if (At(properties, i) is CssKeywordValue keyword && (keyword.Value == "all" || keyword.Value == property)) selected = i;
        if (selected < 0) return new(false, 0, 0, CssCubicEasing.Linear, null);
        CssValue function = At(Required("transition-timing-function"), selected);
        return new(true, Seconds(At(Required("transition-duration"), selected)),
            Seconds(At(Required("transition-delay"), selected)), CssCubicEasing.Resolve(function), function);
    }

    private static int Count(CssValue value) => value is CssListValue { Separator: CssListSeparator.Comma } list ? list.Items.Count : 1;
    private static CssValue At(CssValue value, int index) => value is CssListValue { Separator: CssListSeparator.Comma } list
        ? list.Items[index % list.Items.Count] : value;
    private static double Seconds(CssValue value)
    {
        if (value is not CssDimensionValue time || time.Unit is not ("s" or "ms") || !double.IsFinite(time.Value))
            throw new NotSupportedException("Runtime requires computed CSS time: " + value.Serialize());
        return time.Unit == "ms" ? time.Value / 1000 : time.Value;
    }
}

/// <summary>CSS cubic easing over the existing typed grammar. Unsupported steps/linear() are not approximated.</summary>
public readonly record struct CssCubicEasing(double X1, double Y1, double X2, double Y2)
{
    public static CssCubicEasing Linear => new(0, 0, 1, 1);
    public static CssCubicEasing Resolve(CssValue value)
    {
        if (value is CssKeywordValue keyword) return keyword.Value switch
        {
            "linear" => Linear,
            "ease" => new(.25, .1, .25, 1),
            "ease-in" => new(.42, 0, 1, 1),
            "ease-out" => new(0, 0, .58, 1),
            "ease-in-out" => new(.42, 0, .58, 1),
            _ => throw new NotSupportedException("Unavailable opacity timing function: " + value.Serialize())
        };
        if (value is CssFunctionValue { Name: "cubic-bezier", Arguments: CssListValue arguments } && arguments.Items.Count == 4
            && arguments.Items[0] is CssNumberValue x1 && arguments.Items[1] is CssNumberValue y1
            && arguments.Items[2] is CssNumberValue x2 && arguments.Items[3] is CssNumberValue y2)
            return new(x1.Value, y1.Value, x2.Value, y2.Value);
        throw new NotSupportedException("Unavailable opacity timing function: " + value.Serialize());
    }

    public double Sample(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        if (X1 == Y1 && X2 == Y2) return progress;
        // Monotonic x inversion. Fixed bisection has no allocation and remains stable at flat endpoint tangents.
        double low = 0, high = 1;
        for (int i = 0; i < 52; i++)
        {
            double middle = (low + high) * .5;
            if (Coordinate(middle, X1, X2) < progress) low = middle; else high = middle;
        }
        return Coordinate((low + high) * .5, Y1, Y2);
    }
    private static double Coordinate(double t, double p1, double p2)
    {
        double u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }
}
