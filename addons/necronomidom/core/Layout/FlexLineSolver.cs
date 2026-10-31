using System;

namespace NecronomiDom.Layout;

public readonly record struct FlexMainItem(double Basis, double Minimum, double Maximum, double Grow, double Shrink,
    double MarginStart = 0, double MarginEnd = 0, bool AutoStart = false, bool AutoEnd = false, double Decoration = 0);
public struct FlexMainResult
{
    public double Size;
    public double Offset;
    public double MarginStart;
    public double MarginEnd;
    internal bool Frozen;
    internal double Basis, Minimum, Maximum, Decoration, Fraction, Violation;
}

/// <summary>Single-line CSS flexible lengths, including iterative min/max freezing and scaled shrink factors.
/// Sizes are content sizes; padding/borders are fixed Decoration, never scaled shrink weight.
/// Operates on caller-owned spans and does not allocate.</summary>
public static class FlexLineSolver
{
    public static double Resolve(ReadOnlySpan<FlexMainItem> items, Span<FlexMainResult> results, double available,
        double gap, string justify, bool reverse = false)
    {
        if (results.Length != items.Length || !double.IsFinite(available) || available < 0 || !double.IsFinite(gap) || gap < 0)
            throw new ArgumentOutOfRangeException(nameof(available));
        available = Quantize(available); gap = Quantize(gap);
        double fixedSpace = Math.Max(0, items.Length - 1) * gap, hypothetical = fixedSpace;
        int autoMargins = 0;
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!double.IsFinite(item.Basis) || item.Basis < 0 || !double.IsFinite(item.Minimum) || item.Minimum < 0
                || double.IsNaN(item.Maximum) || item.Maximum < item.Minimum || !double.IsFinite(item.Grow) || item.Grow < 0
                || !double.IsFinite(item.Shrink) || item.Shrink < 0 || !double.IsFinite(item.MarginStart) || !double.IsFinite(item.MarginEnd)
                || !double.IsFinite(item.Decoration) || item.Decoration < 0)
                throw new ArgumentOutOfRangeException(nameof(items));
            ref var result = ref results[i];
            result = new FlexMainResult { Basis = Quantize(item.Basis), Minimum = Quantize(item.Minimum),
                Maximum = Quantize(item.Maximum), Decoration = Quantize(item.Decoration),
                MarginStart = Quantize(item.MarginStart), MarginEnd = Quantize(item.MarginEnd) };
            result.Size = Math.Clamp(result.Basis, result.Minimum, result.Maximum);
            double outer = result.MarginStart + result.MarginEnd + result.Decoration;
            fixedSpace += outer; hypothetical += result.Size + outer;
            autoMargins += (item.AutoStart ? 1 : 0) + (item.AutoEnd ? 1 : 0);
        }
        bool grow = hypothetical < available;
        for (int i = 0; i < items.Length; i++)
        {
            ref var result = ref results[i];
            result.Frozen = (grow ? items[i].Grow : items[i].Shrink) == 0 ||
                (grow ? result.Basis > result.Size : result.Basis < result.Size);
        }
        double initialFree = Free(results, available - fixedSpace);
        for (int pass = 0; pass <= items.Length; pass++)
        {
            double free = Free(results, available - fixedSpace), factors = 0, weights = 0;
            int active = 0;
            for (int i = 0; i < items.Length; i++) if (!results[i].Frozen)
            {
                active++;
                double factor = grow ? items[i].Grow : items[i].Shrink;
                double weight = grow ? factor : factor * results[i].Basis;
                factors += factor; weights += weight;
                results[i].Fraction = weights == 0 ? 0 : weight / weights;
            }
            if (active == 0) break;
            double fractional = Quantize(initialFree * factors);
            if (factors < 1 && Math.Abs(fractional) < Math.Abs(free)) free = fractional;
            double violation = 0;
            // Blink LineFlexer uses reverse prefix fractions and rounds each remaining-space share.
            // This conserves LayoutUnits instead of accumulating independent rounded thirds.
            for (int i = items.Length - 1; i >= 0; i--) if (!results[i].Frozen)
            {
                ref var result = ref results[i];
                double share = result.Fraction == 1 ? free : RoundShare(free * result.Fraction);
                free -= share;
                double target = result.Basis + share;
                result.Size = Math.Clamp(target, result.Minimum, result.Maximum);
                result.Violation = result.Size - target;
                violation += result.Violation;
            }
            for (int i = 0; i < items.Length; i++) if (!results[i].Frozen)
            {
                double delta = results[i].Violation;
                if (violation == 0 || violation > 0 && delta > 0 || violation < 0 && delta < 0) results[i].Frozen = true;
            }
        }
        double remaining = available - fixedSpace;
        for (int i = 0; i < results.Length; i++) remaining -= results[i].Size;
        if (remaining > 0 && autoMargins > 0)
        {
            var margins = new SpaceDistribution(remaining, autoMargins);
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].AutoStart) results[i].MarginStart = margins.Next();
                if (items[i].AutoEnd) results[i].MarginEnd = margins.Next();
            }
            remaining = 0;
        }
        double offset = 0;
        var spacing = default(SpaceDistribution);
        switch (justify)
        {
            case "normal": case "start": case "flex-start": case "flow-start": break;
            case "end": case "flex-end": case "flow-end": offset = remaining; break;
            case "center": offset = Quantize(remaining / 2); break;
            case "space-between":
                if (remaining > 0 && items.Length > 1) spacing = new(remaining, items.Length - 1);
                break;
            case "space-around":
                offset = Quantize(remaining / (remaining > 0 && items.Length > 0 ? 2 * items.Length : 2));
                if (remaining > 0 && items.Length > 1) spacing = new(remaining, items.Length);
                break;
            case "space-evenly":
                offset = Quantize(remaining / (remaining > 0 ? items.Length + 1 : 2));
                if (remaining > 0 && items.Length > 1) spacing = new(remaining, items.Length + 1);
                break;
            default: throw new NotSupportedException("Flex justify-content: " + justify);
        }
        for (int i = 0; i < items.Length; i++)
        {
            offset += results[i].MarginStart;
            results[i].Offset = reverse ? available - offset - results[i].Size - results[i].Decoration : offset;
            offset += results[i].Size + results[i].Decoration + results[i].MarginEnd + gap + spacing.Next();
        }
        return remaining;
    }
    // Blink LayoutUnit's floating constructors truncate toward zero; text intrinsic widths use ceiling.
    internal static double Quantize(double value) => Math.Truncate(value * 64) / 64;
    internal static double CeilIntrinsic(double value) => Math.Ceiling(value * 64) / 64;
    private static double RoundShare(double value) => Math.Round(value * 64, MidpointRounding.AwayFromZero) / 64;
    private static double Free(ReadOnlySpan<FlexMainResult> results, double space)
    {
        for (int i = 0; i < results.Length; i++) space -= results[i].Frozen ? results[i].Size : results[i].Basis;
        return space;
    }
    // Blink LayoutUnitDiffuser: distribute indivisible 1/64px remainders without end gaps.
    private struct SpaceDistribution
    {
        private readonly double _base, _dx, _dy;
        private double _x, _y;
        private int _count;
        internal SpaceDistribution(double space, int count)
        {
            double units = space * 64;
            _base = Math.Truncate(units / count) / 64; _dx = units % count * 2; _dy = count * 2;
            _x = 0; _y = count; _count = count;
        }
        internal double Next()
        {
            if (_count == 0) return 0;
            _count--; _x += _dx;
            if (_x < _y) return _base;
            _y += _dy; return _base + 1d / 64;
        }
    }
}
