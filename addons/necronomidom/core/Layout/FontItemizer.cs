using System;
using System.Collections.Generic;

namespace NecronomiDom.Layout;

/// <summary>A maximal UTF-16 range shaped by one face of a fallback chain.</summary>
public readonly record struct FontFaceRange(int Face, int Start, int End);

/// <summary>Per-code-point face selection: the first chain face mapping the scalar value.</summary>
public static class FontItemizer
{
    /// <summary>
    /// Appends maximal same-face ranges for <paramref name="text"/> and returns 0, or returns the first scalar value no
    /// face maps. C0 controls are never looked up and ASCII the chain lacks joins the current range;
    /// <paramref name="hasGlyph"/>(face, scalar) is only consulted until a face matches.
    /// </summary>
    public static int Itemize(string text, int faceCount, Func<int, int, bool> hasGlyph, List<FontFaceRange> items)
    {
        if (faceCount < 1) throw new ArgumentOutOfRangeException(nameof(faceCount));
        int start = 0, offset = 0, current = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            int face = rune.Value < 0x20 ? current : Select(rune.Value);
            if (face < 0)
            {
                if (rune.Value > 0x7E) return rune.Value;
                face = current;
            }
            if (face != current && offset > start)
            {
                items.Add(new FontFaceRange(current, start, offset));
                start = offset;
            }
            current = face;
            offset += rune.Utf16SequenceLength;
        }
        if (offset > start) items.Add(new FontFaceRange(current, start, offset));
        return 0;

        int Select(int scalar)
        {
            for (int face = 0; face < faceCount; face++)
                if (hasGlyph(face, scalar)) return face;
            return -1;
        }
    }
}
