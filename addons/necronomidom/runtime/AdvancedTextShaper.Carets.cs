using System;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public sealed partial class AdvancedTextShaper
{
    /// <summary>Native caret geometry uses scalar offsets; the DOM exposes UTF-16 offsets.</summary>
    public Rect2 Caret(TextRun text, int utf16Offset)
    {
        var run = GetShapedRun(text);
        using var carets = _server.ShapedTextGetCarets(run.ShapedRid, ContextOffset(text, utf16Offset));
        Rect2 rect = carets["leading_rect"].AsRect2();
        if (rect.Size.Y == 0) rect = carets["trailing_rect"].AsRect2();
        double x = text.InlineFragment != null ? InlineCaretX(text, utf16Offset) : PaintX(run, text.LetterSpacing, rect.Position.X);
        return new Rect2(new Vector2((float)x, rect.Position.Y), rect.Size);
    }
    public Vector2[] Selection(TextRun text, int start, int end)
    {
        if (text.InlineFragment != null)
            return new[] { new Vector2((float)InlineCaretX(text, start), (float)InlineCaretX(text, end)) };
        var run = GetShapedRun(text);
        var spans = _server.ShapedTextGetSelection(run.ShapedRid, ContextOffset(text, start), ContextOffset(text, end));
        for (int i = 0; i < spans.Length; i++)
            spans[i] = new Vector2((float)PaintX(run, text.LetterSpacing, spans[i].X), (float)PaintX(run, text.LetterSpacing, spans[i].Y));
        return spans;
    }
    public int HitTest(TextRun text, double x)
    {
        var run = GetShapedRun(text);
        // Invert retained CSS tracking before invoking the same native hit-test used by Godot text controls.
        double nativeX = x;
        if (text.LetterSpacing != 0)
        {
            double low = 0, high = _server.ShapedTextGetWidth(run.ShapedRid);
            for (int i = 0; i < 24; i++)
            { double middle = (low + high) / 2; if (PaintX(run, text.LetterSpacing, middle) < x) low = middle; else high = middle; }
            nativeX = (low + high) / 2;
        }
        int offset = Utf16Offset(text.ShapingText ?? text.Text, (int)_server.ShapedTextHitTestPosition(run.ShapedRid, nativeX));
        return Math.Clamp(offset - (text.InlineFragment?.Start ?? 0), 0, text.Text.Length);
    }
    private static int ContextOffset(TextRun text, int offset) =>
        ScalarOffset(text.ShapingText ?? text.Text, (text.InlineFragment?.Start ?? 0) + Math.Clamp(offset, 0, text.Text.Length));
    private double InlineCaretX(TextRun text, int offset)
    {
        int start = text.InlineFragment!.Start;
        double left = InlineAdvance(text.ShapingText!, start, text.FontSize, text.FontWeight, text.LetterSpacing, text.FontItalic);
        double right = InlineAdvance(text.ShapingText!, start + Math.Clamp(offset, 0, text.Text.Length),
            text.FontSize, text.FontWeight, text.LetterSpacing, text.FontItalic);
        return left + InlineTextFragment.LayoutWidth(right - left);
    }
    private static int ScalarOffset(string text, int utf16)
    {
        int count = 0;
        for (int i = 0; i < Math.Clamp(utf16, 0, text.Length); count++)
            i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
        return count;
    }
    private static int Utf16Offset(string text, int scalar)
    {
        int offset = 0;
        while (scalar-- > 0 && offset < text.Length)
            offset += char.IsHighSurrogate(text[offset]) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;
        return offset;
    }
    private static double PaintX(NativeShapedTextRun run, double tracking, double nativeX)
    {
        if (tracking == 0) return nativeX;
        double nativeAdvance = 0, shift = 0;
        int clusterEnd = 0; bool tracks = false;
        var glyphs = run.Glyphs;
        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly var glyph = ref glyphs[i];
            if (glyph.Count > 0) { clusterEnd = i + glyph.Count; tracks = false; }
            tracks |= glyph.Index != 0 || glyph.Advance != 0;
            double spacing = i + 1 == clusterEnd && tracks ? tracking : 0;
            for (int repeat = 0; repeat < glyph.Repeat; repeat++)
            {
                nativeAdvance += glyph.Advance - spacing;
                if (nativeX + 0.0001 >= nativeAdvance) shift += spacing;
            }
        }
        return nativeX + shift;
    }
}
