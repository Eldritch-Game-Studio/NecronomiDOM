using System;
using System.Collections.Generic;
using System.Globalization;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    // UAX#14 B2 (break on either side): EM DASH and TWO/THREE-EM DASH.
    // Pinned Chrome keeps adjacent B2 together, but permits a break after intervening ASCII space.
    // Existing ASCII space-only wrapping remains its own unchanged path.
    private static bool BreakBoth(char c) => c is '\u2014' or '\u2E3A' or '\u2E3B';
    private static bool UnicodeBreakAt(string text, int index)
    {
        char left = text[index - 1], right = text[index];
        int before = index - 1;
        while (before > 0 && text[before] == ' ') before--;
        if (BreakBoth(left) && BreakBoth(right)) return false;
        if (left == ' ')
            return char.GetUnicodeCategory(text[before]) != UnicodeCategory.OpenPunctuation;
        if (char.GetUnicodeCategory(right) is UnicodeCategory.ClosePunctuation or UnicodeCategory.FinalQuotePunctuation
            || right is '!' or '?' or ',' or '.' or ':' or ';' or '/' || right == ' ') return false;
        return BreakBoth(left) || BreakBoth(right) && char.GetUnicodeCategory(left) != UnicodeCategory.OpenPunctuation;
    }

    private static bool NextUnicodeChunk(string text, ref int cursor, out int start, out int end)
    {
        while (cursor < text.Length && text[cursor] == ' ') cursor++;
        start = cursor;
        if (start == text.Length) { end = cursor; return false; }
        cursor++;
        while (cursor < text.Length && !UnicodeBreakAt(text, cursor)) cursor++;
        end = cursor;
        while (end > start && text[end - 1] == ' ') end--;
        return true;
    }

    private int WrapUnicodeText(string text, int size, TextShapingParameters shaping,
        double width, List<string>? lines = null)
    {
        int cursor = 0, lineStart = 0, lineEnd = 0, count = 0;
        while (NextUnicodeChunk(text, ref cursor, out int start, out int end))
        {
            string candidate = text[lineStart..end];
            if (lineEnd > lineStart && shaping.Shaper.Measure(candidate, size, shaping.Weight, shaping.Spacing, shaping.Italic).Width > width)
            {
                lines?.Add(text[lineStart..lineEnd]); count++;
                lineStart = start;
            }
            lineEnd = end;
        }
        if (lineEnd > lineStart) { lines?.Add(text[lineStart..lineEnd]); count++; }
        return count;
    }
}
