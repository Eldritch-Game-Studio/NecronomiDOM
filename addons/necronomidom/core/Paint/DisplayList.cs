using System;
using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Paint;

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}
public readonly record struct Rgba(float R, float G, float B, float A);
public readonly record struct BoxStyle(Rgba Background, Rgba Border, double BorderWidth, double Radius);
public abstract record PaintRun;
public sealed record BoxRun(Rect Bounds, BoxStyle Style) : PaintRun;
public sealed record TextRun(string Text, double X, double Baseline, int FontSize, Rgba Color) : PaintRun
{
    /// <summary>DOM element that emitted this run, including plain text without inline fragments.
    /// Logical boxes may overlap after scrolling/clipping; coordinates are not ownership.
    /// Attribution metadata is not part of the run's existing paint-value equality.</summary>
    public Element? Owner { get; init; }
    public double FontWeight { get; init; } = 400;
    public bool FontItalic { get; init; }
    public double LetterSpacing { get; init; }
    public string FontFamily { get; init; } = "";
    public HTMLElement? TextControl { get; init; }
    public int TextOffset { get; init; }
    public Rect ControlLine { get; init; }
    public bool TextControlLineEnd { get; init; } = true;
    public Rect TextControlViewport { get; init; }
    /// <summary>Shared shaping context for an inline fragment. X remains the context's origin.</summary>
    public string? ShapingText { get; init; }
    public InlineTextFragment? InlineFragment { get; init; }

    public bool Equals(TextRun? other) => ReferenceEquals(this, other) || other is not null && base.Equals(other)
        && Text == other.Text && X.Equals(other.X) && Baseline.Equals(other.Baseline)
        && FontSize == other.FontSize && Color.Equals(other.Color)
        && FontWeight.Equals(other.FontWeight) && FontItalic == other.FontItalic
        && LetterSpacing.Equals(other.LetterSpacing) && FontFamily == other.FontFamily
        && Equals(TextControl, other.TextControl) && TextOffset == other.TextOffset
        && ControlLine.Equals(other.ControlLine) && TextControlLineEnd == other.TextControlLineEnd
        && TextControlViewport.Equals(other.TextControlViewport) && ShapingText == other.ShapingText
        && Equals(InlineFragment, other.InlineFragment);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(Text); hash.Add(X); hash.Add(Baseline); hash.Add(FontSize); hash.Add(Color);
        hash.Add(FontWeight); hash.Add(FontItalic); hash.Add(LetterSpacing); hash.Add(FontFamily);
        hash.Add(TextControl); hash.Add(TextOffset); hash.Add(ControlLine); hash.Add(TextControlLineEnd);
        hash.Add(TextControlViewport); hash.Add(ShapingText); hash.Add(InlineFragment);
        return hash.ToHashCode();
    }
}
public enum ImageSampling { Linear, Nearest, Pixelated }
/// <summary>Visible destination and matching intrinsic-pixel crop, already clipped to the content box.</summary>
public sealed record ImageRun(string ResourceKey, Rect Destination, Rect Source, ImageSampling Sampling) : PaintRun;
public sealed partial record LayoutBox(Element Element, Rect Bounds, bool PointerEvents);

/// <summary>A painted DOM text subrange. Start/Length address the shared ShapingText.
/// SourceOffsets maps each retained UTF-16 unit (and its end boundary) back to the original
/// Text node, including collapsed whitespace. Bounds is the fragment's logical selection box.</summary>
public sealed record InlineTextFragment(Text Node, int Start, int Length, Rect Bounds,
    IReadOnlyList<int> SourceOffsets)
{
    /// <summary>Blink ShapeResultView.SnappedWidth: ceil each DOM text item's advance to 1/64px.</summary>
    public static double LayoutWidth(double advance) => Math.Max(0, Math.Ceiling(advance * 64) / 64);
}

/// <summary>CSS paint order retained verbatim; adapters may batch only consecutive box runs.</summary>
public sealed partial class DisplayList
{
    public List<PaintRun> Runs { get; } = new();
    public List<LayoutBox> Boxes { get; } = new();
    /// <summary>False only when no run can paint: empty lists or plain boxes with zero-alpha fill and border.
    /// All other run types conservatively retain rasterization, state bindings and adapter validation.</summary>
    public bool RequiresRasterization
    {
        get
        {
            for (int i = 0; i < Runs.Count; i++)
            {
                if (Runs[i] is not BoxRun box) return true;
                if (box.Style.Background.A != 0 || box.Style.BorderWidth != 0 && box.Style.Border.A != 0)
                    return true;
            }
            return false;
        }
    }
    public Element? HitTest(double x, double y)
    {
        for (int i = Boxes.Count - 1; i >= 0; i--)
            if (Boxes[i].PointerEvents && Boxes[i].ContainsPoint(x, y)) return Boxes[i].Element;
        return null;
    }
}
