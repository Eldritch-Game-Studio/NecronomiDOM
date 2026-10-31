using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private void PaintInlineLine(Element root, CSSStyleDeclaration style, InlineContent content, List<InlineGroup> groups,
        double x, double baseline, double width, bool ellipsis)
    {
        var slices = new List<InlineSlice>();
        for (int i = 0; i < groups.Count; i++) InlineSlices(content, groups[i], i, slices);
        int end = groups[^1].End;
        double markerX = 0;
        var markerShaping = TextShaping(style, TextFontSize(style));
        if (ellipsis)
        {
            double markerWidth = markerShaping.Shaper.Measure("\u2026", TextFontSize(style), markerShaping.Weight, markerShaping.Spacing, markerShaping.Italic).Width;
            double available = Math.Max(0, width - markerWidth);
            end = groups[0].Start;
            foreach (var slice in slices)
                for (int i = slice.Start + 1; i <= slice.End; i++)
                {
                    if (i < content.Text.Length && char.IsLowSurrogate(content.Text[i])) continue;
                    var group = groups[slice.Group];
                    double advance = group.X + InlineSliceAdvance(group, slice, i);
                    if (advance <= available || end == groups[0].Start) { end = i; markerX = advance; }
                    else break;
                }
        }
        // All backgrounds precede glyphs; nested inline backgrounds follow their ancestors.
        foreach (var element in content.Elements)
        {
            var own = _styles.GetComputedStyle(element);
            int size = TextFontSize(own); var shaping = TextShaping(own, size);
            var metrics = shaping.Shaper.Measure(" ", size, shaping.Weight, shaping.Spacing, shaping.Italic);
            double left = double.PositiveInfinity, right = double.NegativeInfinity;
            foreach (var slice in slices)
            {
                if (slice.Start >= end) continue;
                bool descendant = false;
                for (Element? ancestor = slice.Piece.Owner; ancestor != null && ancestor != root; ancestor = ancestor.ParentElement)
                    if (ancestor == element) { descendant = true; break; }
                if (!descendant) continue;
                var group = groups[slice.Group];
                left = Math.Min(left, group.X + slice.Left);
                right = Math.Max(right, group.X + InlineSliceAdvance(group, slice, Math.Min(slice.End, end)));
            }
            if (!double.IsFinite(left)) continue;
            var bounds = new Rect(x + left, baseline - metrics.Ascent, right - left, metrics.Ascent + metrics.Descent);
            _paintHitOrder.Add(_result.Boxes.Count);
            _result.Boxes.Add(new LayoutBox(element, bounds, own.GetPropertyValue("pointer-events") != "none")
            { UsedGeometry = new UsedLayoutGeometry(default, null, PositionedMode.Static, null, default, new UsedBoxTransform(Affine2D.Identity, false)) });
            var background = SampleBackgroundColor(element, Color(own, "background-color"));
            if (background.A > 0) _result.Runs.Add(new BoxRun(bounds, new BoxStyle(background, default, 0, 0)));
        }
        foreach (var slice in slices)
        {
            int start = slice.Start, finish = Math.Min(slice.End, end);
            if (start >= finish) continue;
            var group = groups[slice.Group]; var piece = slice.Piece;
            double right = finish == slice.End ? slice.RawRight : InlineAdvance(group, finish);
            double logicalWidth = finish == slice.End ? slice.Width : InlineTextFragment.LayoutWidth(right - slice.RawLeft);
            var bounds = new Rect(x + group.X + slice.Left, baseline - group.Metrics.Ascent, logicalWidth,
                group.Metrics.Ascent + group.Metrics.Descent);
            int sourceStart = start - piece.Start, length = finish - start;
            var offsets = new int[length + 1];
            piece.Offsets.CopyTo(sourceStart, offsets, 0, length);
            offsets[length] = piece.Offsets[sourceStart + length - 1] + 1;
            var fragment = new InlineTextFragment(piece.Node, start - group.Start, length, bounds, offsets);
            var color = SampleForegroundColor(piece.Owner, piece.Style);
            var effects = SampleForegroundEffects(piece.Owner, color);
            // Each DOM item's logical advance is snapped independently, while glyph positions remain
            // borrowed from the original shared shape (no substring shaping or synthetic word spacing).
            double shapeX = x + group.X + slice.Left - slice.RawLeft;
            var run = new TextRun(content.Text[start..finish], shapeX, baseline, group.Size, color)
            { Owner = piece.Owner, FontWeight = group.Shaping.Weight, LetterSpacing = group.Shaping.Spacing, FontFamily = group.Shaping.Family,
                FontItalic = group.Shaping.Italic, ShapingText = group.Text, InlineFragment = fragment };
            Rect ink = group.Shaping.Shaper is IInlineTextShaper native
                ? native.InlineInkBounds(group.Text, group.Size, group.Shaping.Weight, group.Shaping.Spacing, group.Shaping.Italic)
                : new Rect(0, -group.Metrics.Ascent, group.Metrics.Width, group.Metrics.Ascent + group.Metrics.Descent);
            double pad = effects.StrokeWidth;
            foreach (var shadow in effects.Shadows.Span)
                pad = Math.Max(pad, Math.Max(Math.Abs(shadow.OffsetX), Math.Abs(shadow.OffsetY)) + shadow.BlurRadius * 2 + effects.StrokeWidth);
            double clipLeft = start == group.Start ? Math.Min(ink.X - pad, 0) : slice.RawLeft;
            double clipRight = finish == group.End ? Math.Max(ink.X + ink.Width + pad, right) : right;
            _result.Runs.Add(new BeginRectClipRun(new Rect(shapeX + clipLeft, baseline + ink.Y - pad,
                Math.Max(0, clipRight - clipLeft), ink.Height + pad * 2)));
            _result.Runs.Add(effects.HasEffects ? new TextEffectsRun(run, effects) : run);
            _result.Runs.Add(new EndRectClipRun());
        }
        if (ellipsis)
        {
            var color = SampleForegroundColor(root, style);
            PaintTextGlyphs(root, "\u2026", x + markerX, baseline, TextFontSize(style), color, SampleForegroundEffects(root, color), markerShaping);
        }
    }
}
