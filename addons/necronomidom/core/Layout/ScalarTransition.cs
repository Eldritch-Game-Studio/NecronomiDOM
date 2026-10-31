using System;

namespace NecronomiDom.Layout;

/// <summary>
/// One host-advanced numeric CSS transition (for a channel the host does not animate itself, such as the pickup toast's
/// <c>translate</c>) on a monotonic seconds clock. The value is a function of elapsed time only: every frame rate samples
/// the same curve, and a frame longer than the duration lands exactly on the target instead of stepping per frame.
/// </summary>
public readonly record struct ScalarTransition(double From, double Target, double StartTime, double Duration, CssCubicEasing Easing)
{
    /// <summary>Elapsed fraction in [0, 1]; a non-positive duration completes immediately.</summary>
    public double Progress(double now) => Duration > 0 ? Math.Clamp((now - StartTime) / Duration, 0, 1) : 1;

    public double Sample(double now) => From + (Target - From) * Easing.Sample(Progress(now));

    public bool IsComplete(double now) => Progress(now) >= 1;
}
