using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Layout;

/// <summary>The individual scale longhand and existing typed transition timing lists.</summary>
public readonly record struct ScaleTransitionStyle(AnimatedScale? Scale, bool Enabled, double Duration,
    double Delay, CssCubicEasing Easing)
{
    internal string? TimingKeyword { get; init; }
    public static ScaleTransitionStyle Resolve(CSSStyleDeclaration computedValues)
    {
        var timing = TransitionTimingStyle.Resolve(computedValues, "scale");
        CssValue value = computedValues.GetSpecifiedValue("scale")
            ?? throw new NotSupportedException("Missing computed scale.");
        AnimatedScale? scale = null;
        if (value is not CssKeywordValue { Value: "none" })
        {
            int count = value is CssListValue list ? list.Items.Count : 1;
            CssValue At(int index) => value is CssListValue items ? items.Items[index] : value;
            if (count is < 1 or > 3 || count == 3 && Number(At(2)) != 1)
                throw new NotSupportedException("Geometry transitions require genuine 2D scale.");
            double x = Number(At(0));
            scale = new(x, count >= 2 ? Number(At(1)) : x);
        }
        return new(scale, timing.Enabled, timing.Duration, timing.Delay, timing.Easing)
        { TimingKeyword = (timing.TimingFunction as CssKeywordValue)?.Value };
    }
    private static double Number(CssValue value)
    {
        if (!CSSNumericValue.TryFrom(value, out var numeric)) throw new NotSupportedException("Unresolved scale transition coordinate.");
        double number = numeric.To("number").Value;
        if (!double.IsFinite(number)) throw new NotSupportedException("Nonfinite scale transition coordinate.");
        return number;
    }
}
