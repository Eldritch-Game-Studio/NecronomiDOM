using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private sealed class InlinePiece(Text node, Element owner, CSSStyleDeclaration style, int start)
    {
        internal readonly Text Node = node;
        internal readonly Element Owner = owner;
        internal readonly CSSStyleDeclaration Style = style;
        internal readonly int Start = start;
        internal readonly List<int> Offsets = new();
        internal int End => Start + Offsets.Count;
    }
    private sealed record InlineContent(string Text, List<InlinePiece> Pieces, List<Element> Elements);
    private readonly record struct InlineGroup(int Start, int End, int Size, TextShapingParameters Shaping,
        TextMetrics Metrics, double Height, string Text, double X, double Width);
    private readonly record struct InlineSlice(int Group, InlinePiece Piece, int Start, int End,
        double RawLeft, double RawRight, double Left, double Width);

    private bool HasInlineChildren(Element element)
    {
        foreach (var node in element.ChildNodes)
            if (node is Element child && _styles.GetComputedStyle(child).GetPropertyValue("display") == "inline"
                && _styles.GetComputedStyle(child).GetPropertyValue("position") != "absolute") return true;
        return false;
    }
    private InlineContent CollectInlineContent(Element root, CSSStyleDeclaration rootStyle)
    {
        var text = new StringBuilder();
        var pieces = new List<InlinePiece>();
        var elements = new List<Element>();
        (Text Node, Element Owner, CSSStyleDeclaration Style, int Offset)? pending = null;
        void Append(Text node, Element owner, CSSStyleDeclaration style, int offset, char value)
        {
            if (pieces.Count == 0 || pieces[^1].Node != node || pieces[^1].End != text.Length)
                pieces.Add(new InlinePiece(node, owner, style, text.Length));
            pieces[^1].Offsets.Add(offset); text.Append(value);
        }
        void Visit(Element element, CSSStyleDeclaration style)
        {
            foreach (var child in element.ChildNodes)
            {
                if (child is Text node)
                {
                    for (int i = 0; i < node.Data.Length; i++)
                    {
                        char value = node.Data[i];
                        if (value is ' ' or '\t' or '\n' or '\r' or '\f')
                        {
                            if (text.Length != 0) pending ??= (node, element, style, i);
                            continue;
                        }
                        if (pending is { } space)
                        { Append(space.Node, space.Owner, space.Style, space.Offset, ' '); pending = null; }
                        Append(node, element, style, i, value);
                    }
                }
                else if (child is Element nested)
                {
                    var computed = _styles.GetComputedStyle(nested);
                    if (computed.GetPropertyValue("display") == "none" || computed.GetPropertyValue("position") == "absolute") continue;
                    if (computed.GetPropertyValue("display") != "inline") Fail(nested, "children", "mixed block and inline");
                    ValidateInlineElement(nested, computed, rootStyle);
                    elements.Add(nested); Visit(nested, computed);
                }
            }
        }
        Visit(root, rootStyle);
        string value = text.ToString(); ValidateText(root, value);
        return new InlineContent(value, pieces, elements);
    }
    private void ValidateInlineElement(Element element, CSSStyleDeclaration style, CSSStyleDeclaration rootStyle)
    {
        Validate(element, style);
        if (element.LocalName is not ("span" or "a")) Fail(element, "inline element", "only text spans and anchors are supported");
        // Inline box edges, positioning and independent compositing are not silently treated as text styles.
        foreach (string side in new[] { "top", "right", "bottom", "left" })
            foreach (string property in new[] { "margin-" + side, "padding-" + side, "border-" + side + "-width" })
                if (Length(style, property, 0) != 0) Fail(element, property, "inline box edges are not supported");
        foreach (string property in new[] { "position", "opacity", "transform", "translate", "rotate", "scale", "clip-path", "background-image" })
            if (style.GetPropertyValue(property) != InitialText(property)) Fail(element, property, "inline compositing is not supported");
        if (style.GetPropertyValue("text-wrap-mode") != rootStyle.GetPropertyValue("text-wrap-mode"))
            Fail(element, "text-wrap-mode", "nested wrap-mode changes are not supported");
    }
    private List<InlineGroup> InlineGroups(InlineContent content, int start, int end)
    {
        var groups = new List<InlineGroup>();
        int groupStart = start, groupEnd = start, groupSize = 0;
        TextShapingParameters groupShaping = default;
        double groupHeight = 0, x = 0;
        void Flush()
        {
            if (groupStart == groupEnd) return;
            string value = content.Text[groupStart..groupEnd];
            var metrics = groupShaping.Shaper.Measure(value, groupSize, groupShaping.Weight, groupShaping.Spacing, groupShaping.Italic);
            var group = new InlineGroup(groupStart, groupEnd, groupSize, groupShaping, metrics, groupHeight, value, x, 0);
            double width = InlineSlices(content, group, groups.Count);
            groups.Add(group with { Width = width });
            x += width;
        }
        foreach (var piece in content.Pieces)
        {
            if (piece.End <= start || piece.Start >= end) continue;
            int size = TextFontSize(piece.Style);
            var shaping = TextShaping(piece.Style, size);
            double height = TextLineHeight(piece.Style, size, shaping);
            if (groupEnd > groupStart && (groupSize != size || groupShaping != shaping))
            { Flush(); groupStart = groupEnd; groupHeight = 0; }
            groupSize = size; groupShaping = shaping;
            groupEnd = Math.Min(piece.End, end); groupHeight = Math.Max(groupHeight, height);
        }
        Flush();
        return groups;
    }
    private double InlineWidth(InlineContent content, int start, int end)
    {
        var groups = InlineGroups(content, start, end);
        return groups.Count == 0 ? 0 : groups[^1].X + groups[^1].Width;
    }
    private PositionedIntrinsicWidth InlineIntrinsic(Element element, CSSStyleDeclaration style)
    {
        var content = CollectInlineContent(element, style);
        double maximum = InlineWidth(content, 0, content.Text.Length);
        if (style.GetPropertyValue("text-wrap-mode") == "nowrap") return new(maximum, maximum);
        double minimum = 0; int cursor = 0;
        while (NextUnicodeChunk(content.Text, ref cursor, out int start, out int end))
            minimum = Math.Max(minimum, InlineWidth(content, start, end));
        return new(minimum, maximum);
    }
    private double LayoutInlineFragments(Element root, CSSStyleDeclaration style, double x, double y, double width, bool paint)
    {
        var content = CollectInlineContent(root, style);
        if (content.Text.Length == 0) return 0;
        var lines = new List<(int Start, int End)>();
        if (style.GetPropertyValue("text-wrap-mode") == "nowrap") lines.Add((0, content.Text.Length));
        else
        {
            int cursor = 0, lineStart = 0, lineEnd = 0;
            while (NextUnicodeChunk(content.Text, ref cursor, out int start, out int end))
            {
                if (lineEnd > lineStart && InlineWidth(content, lineStart, end) > width)
                { lines.Add((lineStart, lineEnd)); lineStart = start; }
                lineEnd = end;
            }
            if (lineEnd > lineStart) lines.Add((lineStart, lineEnd));
        }
        int rootSize = TextFontSize(style); var rootShaping = TextShaping(style, rootSize);
        var strut = rootShaping.Shaper.Measure(" ", rootSize, rootShaping.Weight, rootShaping.Spacing, rootShaping.Italic);
        double rootHeight = TextLineHeight(style, rootSize, rootShaping), originY = y;
        foreach (var line in lines)
        {
            var groups = InlineGroups(content, line.Start, line.End);
            double above = InlineAscent(strut, rootHeight);
            double below = rootHeight - above;
            foreach (var group in groups)
            {
                double top = InlineAscent(group.Metrics, group.Height);
                above = Math.Max(above, top); below = Math.Max(below, group.Height - top);
            }
            if (paint)
            {
                double total = groups[^1].X + groups[^1].Width;
                double offset = Math.Max(0, style.GetPropertyValue("text-align") switch
                { "center" => (width - total) / 2, "right" or "end" => width - total, _ => 0 });
                bool ellipsis = style.GetPropertyValue("text-overflow") == "ellipsis"
                    && style.GetPropertyValue("overflow-x") != "visible" && total > width;
                PaintInlineLine(root, style, content, groups, x + offset, y + above, width, ellipsis);
            }
            y += above + below;
        }
        return y - originY;
    }
    private static double InlineAdvance(InlineGroup group, int offset)
    {
        if (offset == group.Start) return 0;
        if (offset == group.End) return group.Metrics.Width;
        if (group.Shaping.Shaper is not IInlineTextShaper shaper)
            throw new NotSupportedException("Inline fragments require a shaper with retained subrange geometry.");
        return shaper.InlineAdvance(group.Text, offset - group.Start, group.Size,
            group.Shaping.Weight, group.Shaping.Spacing, group.Shaping.Italic);
    }
    // Pinned Blink 155 line_utils.cc::CalculateLeadingSpace floors the ascent half-leading;
    // all fractional/odd remaining leading belongs below the baseline, including negative leading.
    private static double InlineAscent(TextMetrics metrics, double height) =>
        metrics.Ascent + Math.Floor((height - metrics.Ascent - metrics.Descent) / 2);

    private static double InlineSlices(InlineContent content, InlineGroup group, int index, List<InlineSlice>? slices = null)
    {
        double left = 0;
        foreach (var piece in content.Pieces)
        {
            int start = Math.Max(piece.Start, group.Start), end = Math.Min(piece.End, group.End);
            if (start >= end) continue;
            double rawLeft = InlineAdvance(group, start), rawRight = InlineAdvance(group, end);
            double width = InlineTextFragment.LayoutWidth(rawRight - rawLeft);
            slices?.Add(new InlineSlice(index, piece, start, end, rawLeft, rawRight, left, width));
            left += width;
        }
        return left;
    }
    private static double InlineSliceAdvance(InlineGroup group, InlineSlice slice, int offset) =>
        slice.Left + InlineTextFragment.LayoutWidth(InlineAdvance(group, offset) - slice.RawLeft);
}
