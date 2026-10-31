using System;

namespace NecronomiDom.Runtime;

/// <summary>
///     Held-input repeat timing that keeps a 60 fps cadence at every frame rate: a delay/interval becomes whole
///     1/60 s ticks, and each repeat carries the overshoot from the previous deadline instead of restarting from the
///     frame that fired, so the period holds on average at any frame rate.
/// </summary>
internal static class RepeatCadence
{
    private const double ReferenceTick = 1.0 / 60.0;

    /// <summary>A deadline this close after a frame is due on that frame; absorbs float/millisecond clock error.</summary>
    private const double Tolerance = 0.001;

    /// <summary>The 60 fps period of a delay/interval, in whole reference ticks rounded up (0.1 s → 6 ticks).</summary>
    public static double Period(double seconds) =>
        Math.Max(0, Math.Ceiling(seconds / ReferenceTick - 0.001)) * ReferenceTick;

    public static bool Due(double now, double deadline) => now >= deadline - Tolerance;

    /// <summary>The deadline after one that fired at <paramref name="now"/>: carries the overshoot, but restarts from
    /// now when it would already be due again (a hitch), so a stall never bursts repeats.</summary>
    public static double Next(double deadline, double period, double now)
    {
        var next = deadline + period;
        return Due(now, next) ? now + period : next;
    }
}
