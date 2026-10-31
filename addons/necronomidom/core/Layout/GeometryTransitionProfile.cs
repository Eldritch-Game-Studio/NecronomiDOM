using System;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

/// <summary>
/// Immutable document opt-in. <see cref="Css"/> uses CSS Transitions semantics; <see cref="Simplified"/> uses
/// float32 targets and lerps, tolerance-based equality, px/% start conversion against the previous parent width
/// and polynomial keyword easings. Ordinary CSS never silently receives simplified arithmetic.
/// </summary>
public enum GeometryTransitionProfile { Css, Simplified }

/// <summary>A sampled CSS length-percentage, resolved only against the actual current layout basis.</summary>
public readonly record struct AnimatedWidth(double Pixels, double Percent, bool HasPercentage = false)
{
    public double Resolve(double percentageBasis)
    {
        double value = Pixels + Percent * percentageBasis / 100;
        if (!double.IsFinite(value)) throw new NotSupportedException("Nonfinite animated used width.");
        return value;
    }
}

public interface IAnimatedWidthSource
{
    bool TryGetWidth(Element element, out AnimatedWidth width);
}

/// <summary>Individual 2D scale coordinates, not an additional transform-list multiplier.</summary>
public readonly record struct AnimatedScale(double X, double Y)
{
    public static AnimatedScale Identity => new(1, 1);
}

public interface IAnimatedScaleSource
{
    bool TryGetScale(Element element, out AnimatedScale scale);
}
