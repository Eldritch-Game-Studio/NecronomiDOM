using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>CSS Images used geometry and stop fixup, consuming the existing computed image AST.</summary>
public static class GradientLayout
{
    public static GradientImage Resolve(CssGradientValue value, double width, double height)
    {
        if (!(width > 0 && height > 0) || !double.IsFinite(width + height))
            throw new ArgumentOutOfRangeException(nameof(width));
        GradientKind kind = value.Name.EndsWith("linear-gradient", StringComparison.Ordinal) ? GradientKind.Linear
            : value.Name.EndsWith("radial-gradient", StringComparison.Ordinal) ? GradientKind.Radial : GradientKind.Conic;
        var items = value.Prelude is CssListValue list ? list.Items
            : value.Prelude is { } single ? new[] { single } : Array.Empty<CssValue>();
        int end = items.Count, at = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (Keyword(items[i], "in")) { end = i; break; }
            if (Keyword(items[i], "at")) at = i;
        }
        double x = width / 2, y = height / 2, ax = 0, ay = 1, length = height;
        bool circle = false;
        if (at >= 0)
        {
            if (at + 3 != end) throw new NotSupportedException("Gradient position must be a computed two-axis position.");
            x = ImageLayout.Length(items[at + 1], width); y = ImageLayout.Length(items[at + 2], height);
        }
        if (kind == GradientKind.Linear)
        {
            double angle = Math.PI;
            if (end > 0 && Keyword(items[0], "to"))
            {
                double horizontal = 0, vertical = 0;
                for (int i = 1; i < end; i++)
                    if (items[i] is CssKeywordValue k)
                        switch (k.Value) { case "left": horizontal = -1; break; case "right": horizontal = 1; break;
                            case "top": vertical = -1; break; case "bottom": vertical = 1; break; }
                // Corner gradients are perpendicular to the diagonal joining the adjacent corners (magic corners).
                angle = horizontal != 0 && vertical != 0 ? Math.Atan2(horizontal * height, -vertical * width)
                    : Math.Atan2(horizontal, -vertical);
            }
            else if (end > 0) angle = (Angle(items[0]) % 360) * Math.PI / 180;
            ax = Math.Sin(angle); ay = -Math.Cos(angle);
            length = Math.Abs(width * ax) + Math.Abs(height * ay);
            x -= ax * length / 2; y -= ay * length / 2;
        }
        else if (kind == GradientKind.Conic)
        {
            ax = end > 1 && Keyword(items[0], "from") ? (Angle(items[1]) % 360) / 360 : 0;
            ay = 0; length = 1;
        }
        else
        {
            string sizing = "farthest-corner";
            var radii = new List<CssValue>(2);
            for (int i = 0; i < (at < 0 ? end : at); i++)
                if (items[i] is CssKeywordValue k)
                { if (k.Value == "circle") circle = true; else if (k.Value != "ellipse") sizing = k.Value; }
                else radii.Add(items[i]);
            if (radii.Count > 0)
            {
                circle = radii.Count == 1;
                ax = Math.Max(0, ImageLayout.Length(radii[0], width));
                ay = radii.Count == 1 ? ax : Math.Max(0, ImageLayout.Length(radii[1], height));
            }
            else
            {
                bool closest = sizing.StartsWith("closest", StringComparison.Ordinal);
                double left = Math.Abs(x), right = Math.Abs(width - x), top = Math.Abs(y), bottom = Math.Abs(height - y);
                ax = closest ? Math.Min(left, right) : Math.Max(left, right);
                ay = closest ? Math.Min(top, bottom) : Math.Max(top, bottom);
                if (circle)
                {
                    double radius = closest ? Math.Min(ax, ay) : Math.Max(ax, ay);
                    if (sizing.EndsWith("corner", StringComparison.Ordinal)) radius = Math.Sqrt(ax * ax + ay * ay);
                    ax = ay = radius;
                }
                else if (sizing.EndsWith("corner", StringComparison.Ordinal))
                {
                    // The chosen corner is on an ellipse with the same aspect ratio as the side ellipse.
                    double factor = Math.Sqrt(2);
                    ax *= factor; ay *= factor;
                }
            }
            length = ax;
        }
        return new(kind, value.Name.StartsWith("repeating-", StringComparison.Ordinal), x, y, ax, ay, length,
            value.InterpolationSpace, value.HueMethod, FixStops(value, kind, length), circle);
    }

    private static IReadOnlyList<GradientStop> FixStops(CssGradientValue gradient, GradientKind kind, double length)
    {
        var entries = new List<(CssResolvedColor? Color, double? Position)>(gradient.Stops.Count * 2);
        var context = new CssColorContext();
        foreach (var stop in gradient.Stops)
        {
            CssResolvedColor? color = null;
            if (stop.Color is { } typed)
            {
                if (!typed.TryResolve(context, out var resolved) || typed.RequiresCurrentColor)
                    throw new NotSupportedException("Gradient colors must be computed before used-value resolution.");
                color = resolved;
            }
            if (stop.Positions.Count == 0) entries.Add((color, null));
            else foreach (var position in stop.Positions)
                entries.Add((color, Coordinate(position, kind, length)));
        }
        entries[0] = (entries[0].Color, entries[0].Position ?? 0);
        entries[^1] = (entries[^1].Color, entries[^1].Position ?? 1);
        double previous = double.NegativeInfinity;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Position is { } position)
            { previous = Math.Max(previous, position); entries[i] = (entries[i].Color, previous); }
        for (int i = 1; i < entries.Count - 1; i++)
        {
            if (entries[i].Position.HasValue) continue;
            int start = i - 1, end = i + 1;
            while (!entries[end].Position.HasValue) end++;
            double a = entries[start].Position!.Value, b = entries[end].Position!.Value;
            for (; i < end; i++) entries[i] = (entries[i].Color, a + (b - a) * (i - start) / (end - start));
        }
        var stops = new List<GradientStop>(entries.Count);
        foreach (var entry in entries)
            if (entry.Color is { } color) stops.Add(new(entry.Position!.Value, color));
            else stops[^1] = stops[^1] with { HintAfter = entry.Position };
        return stops.ToArray();
    }

    private static double Coordinate(CssValue value, GradientKind kind, double length)
    {
        if (kind != GradientKind.Conic)
        {
            // Degenerate radial geometry is handled by the renderer; retaining percentages also avoids 0/0.
            if (value is CssPercentageValue p) return p.Value / 100;
            return ImageLayout.Length(value, length) / (length > 0 ? length : 1e-12);
        }
        if (value is CssPercentageValue percent) return percent.Value / 100;
        if (value is CssMathValue math && math.TryEvaluate(new CssMathEvaluationContext(percentageBasis: 360,
            percentageHint: CssNumericDimension.Angle), out double degrees)) return degrees / 360;
        return Angle(value) / 360;
    }

    private static double Angle(CssValue value)
    {
        double angle = value switch
        {
            CssDimensionValue { Unit: "deg" } d => d.Value,
            CssDimensionValue { Unit: "grad" } d => d.Value * .9,
            CssDimensionValue { Unit: "rad" } d => d.Value * 180 / Math.PI,
            CssDimensionValue { Unit: "turn" } d => d.Value * 360,
            CssNumberValue { Value: 0 } => 0,
            CssMathValue math when math.TryEvaluate(new CssMathEvaluationContext(percentageHint: CssNumericDimension.Angle), out double result) => result,
            _ => throw new NotSupportedException("Unresolved gradient angle.")
        };
        if (!double.IsFinite(angle)) throw new NotSupportedException("Nonfinite gradient angle.");
        return angle;
    }
    private static bool Keyword(CssValue value, string keyword) => value is CssKeywordValue k && k.Is(keyword);
}
