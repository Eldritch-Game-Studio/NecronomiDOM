using System;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Paint;

/// <summary>CSS interpolation is shared with color-mix: missing components, hue routes and premultiplied alpha.</summary>
public static class GradientSampling
{
    public static Rgba Segment(GradientImage image, int index, double progress)
    {
        // Endpoint extension must retain missing-component carry from the adjacent real segment.
        if (index == image.Stops.Count - 1) { index--; progress = 1; }
        var a = image.Stops[index]; var b = image.Stops[index + 1];
        var rgb = CssColorInterpolation.Interpolate(a.Color, b.Color, image.InterpolationSpace, image.HueMethod, progress).ToSrgb();
        static float Channel(double x) => (float)(double.IsNaN(x) ? 0 : Math.Clamp(x, 0, 1));
        return new(Channel(rgb.C1), Channel(rgb.C2), Channel(rgb.C3), Channel(rgb.Alpha));
    }

    public static double HintProgress(double progress, double hint)
    {
        if (hint <= 0) return progress > 0 ? 1 : 0;
        if (hint >= 1) return progress < 1 ? 0 : 1;
        return Math.Pow(progress, Math.Log(.5) / Math.Log(hint));
    }

    public static Rgba Sample(GradientImage image, double position)
    {
        var stops = image.Stops;
        double first = stops[0].Position, last = stops[^1].Position;
        if (image.Repeating && last <= first) return Average(image);
        if (image.Repeating && last > first) position = first + (position - first - Math.Floor((position - first) / (last - first)) * (last - first));
        int i = 0;
        while (i + 1 < stops.Count && position >= stops[i + 1].Position) i++;
        if (position < first) return Segment(image, 0, 0);
        if (i == stops.Count - 1) return Segment(image, i, 0);
        double width = stops[i + 1].Position - stops[i].Position;
        double progress = width > 0 ? (position - stops[i].Position) / width : 1;
        if (stops[i].HintAfter is { } hint) progress = HintProgress(progress, (hint - stops[i].Position) / width);
        return Segment(image, i, progress);
    }
    /// <summary>CSS Images average: integrate premultiplied sRGBA endpoint pairs, with equal spacing for a zero period.</summary>
    public static Rgba Average(GradientImage image)
    {
        double length = image.Stops[^1].Position - image.Stops[0].Position;
        double red = 0, green = 0, blue = 0, alpha = 0;
        for (int i = 0; i + 1 < image.Stops.Count; i++)
        {
            double weight = length > 0 ? (image.Stops[i + 1].Position - image.Stops[i].Position) / length / 2
                : .5 / (image.Stops.Count - 1);
            var a = Segment(image, i, 0); var b = Segment(image, i, 1);
            red += (a.R * a.A + b.R * b.A) * weight;
            green += (a.G * a.A + b.G * b.A) * weight;
            blue += (a.B * a.A + b.B * b.A) * weight;
            alpha += (a.A + b.A) * weight;
        }
        return alpha > 0 ? new((float)(red / alpha), (float)(green / alpha), (float)(blue / alpha), (float)alpha) : default;
    }
}
