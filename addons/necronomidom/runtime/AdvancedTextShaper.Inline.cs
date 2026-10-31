using System;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public sealed partial class AdvancedTextShaper
{
    public double InlineAdvance(string text, int offset, int fontSize, double fontWeight = 400,
        double letterSpacing = 0, bool italic = false)
    {
        if (offset < 0 || offset > text.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var shaped = Shape(text, fontSize, fontWeight, letterSpacing, italic);
        if (offset == 0) return 0;
        if (offset == text.Length) return shaped.Metrics.Width;
        // Blink ShapeResultView selects glyphs by their cluster's start character, not the
        // intra-ligature caret. A cluster crossing DOM nodes belongs wholly to its first node.
        int scalar = ScalarOffset(text, offset);
        double advance = 0;
        foreach (ref readonly var glyph in shaped.Run.Glyphs)
            if (glyph.Start < scalar) advance += glyph.Advance * glyph.Repeat;
        return advance;
    }
    public Rect InlineInkBounds(string text, int fontSize, double fontWeight = 400,
        double letterSpacing = 0, bool italic = false)
    {
        var cached = Shape(text, fontSize, fontWeight, letterSpacing, italic);
        Rect2 ink;
        if (cached.Ink is Rect2 ready) ink = ready;
        else
        {
            ink = cached.Run.Ink().Fill;
            _runs[(text, fontSize, fontWeight, letterSpacing, italic)] = (cached.Run, ink, cached.Metrics, _layoutPass);
        }
        return new Rect(ink.Position.X, ink.Position.Y, ink.Size.X, ink.Size.Y);
    }
}
