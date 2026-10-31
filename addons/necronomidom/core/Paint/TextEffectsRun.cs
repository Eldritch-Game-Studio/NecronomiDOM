using System;

namespace NecronomiDom.Paint;

public readonly record struct TextShadow(double OffsetX, double OffsetY, double BlurRadius, Rgba Color);

/// <summary>Computed CSS text effects, in CSS pixels. Shadows retain CSS order (first is topmost).
/// Native adapters paint these with the same shaped glyphs as the fill, never the line box.</summary>
public readonly record struct TextEffectsStyle
{
    public double StrokeWidth { get; }
    public Rgba StrokeColor { get; }
    public ReadOnlyMemory<TextShadow> Shadows { get; }
    public bool HasEffects => StrokeWidth > 0 || !Shadows.IsEmpty;

    public TextEffectsStyle(double strokeWidth, Rgba strokeColor, ReadOnlyMemory<TextShadow> shadows)
    {
        if (!double.IsFinite(strokeWidth) || strokeWidth < 0) throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        Validate(strokeColor);
        foreach (var shadow in shadows.Span)
        {
            if (!double.IsFinite(shadow.OffsetX) || !double.IsFinite(shadow.OffsetY)
                || !double.IsFinite(shadow.BlurRadius) || shadow.BlurRadius < 0)
                throw new ArgumentOutOfRangeException(nameof(shadows));
            Validate(shadow.Color);
        }
        StrokeWidth = strokeWidth; StrokeColor = strokeColor; Shadows = shadows;
    }
    private static void Validate(Rgba color)
    {
        if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B)
            || !float.IsFinite(color.A) || color.A < 0 || color.A > 1)
            throw new ArgumentOutOfRangeException(nameof(color));
    }
}

/// <summary>One ordered glyph paint primitive: shadows back-to-front, fill, then centered stroke.
/// The supplied TextRun owns the actual text, baseline, font size and fill color.</summary>
public sealed record TextEffectsRun(TextRun Text, TextEffectsStyle Effects) : PaintRun;
