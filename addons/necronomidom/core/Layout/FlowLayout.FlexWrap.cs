using System;
using System.Buffers;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private struct FlexWrapLine
    {
        public int Start, End;
        public double Cross, Offset;
    }
    private readonly struct FlexWrapScratch : IDisposable
    {
        private readonly FlexWrapLine[] _lines;
        private readonly int _capacity;
        public FlexWrapScratch(int capacity)
        {
            _capacity = capacity;
            _lines = capacity == 0 ? Array.Empty<FlexWrapLine>() : ArrayPool<FlexWrapLine>.Shared.Rent(capacity);
        }
        public Span<FlexWrapLine> Values => _lines.AsSpan(0, _capacity);
        public void Dispose() { if (_capacity > 0) ArrayPool<FlexWrapLine>.Shared.Return(_lines); }
    }
    private static int ResolveWrappedMain(ReadOnlySpan<FlexMainItem> items, Span<FlexMainResult> used,
        Span<FlexWrapLine> lines, double available, double gap, string justify, bool reverse)
    {
        int count = 0, start = 0;
        double occupied = 0;
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            double outer = Math.Clamp(item.Basis, item.Minimum, item.Maximum) + item.Decoration + item.MarginStart + item.MarginEnd;
            // Break using outer hypothetical sizes, BEFORE distributing flexible lengths.
            // An oversized first item remains on its own line, including zero-width containers.
            if (i > start && occupied + gap + outer > available)
            {
                lines[count++] = new FlexWrapLine { Start = start, End = i };
                FlexLineSolver.Resolve(items[start..i], used[start..i], available, gap, justify, reverse);
                start = i; occupied = 0;
            }
            occupied += (i > start ? gap : 0) + outer;
        }
        if (start < items.Length)
        {
            lines[count++] = new FlexWrapLine { Start = start, End = items.Length };
            FlexLineSolver.Resolve(items[start..], used[start..], available, gap, justify, reverse);
        }
        return count;
    }
    private double AlignWrappedLines(Element e, CSSStyleDeclaration style, PositionedBoxPlan plan,
        List<FlexChild> children, Span<FlexWrapLine> lines)
    {
        double gap = FlexGap(style, "row-gap", plan.DefiniteContentHeight);
        double natural = Math.Max(0, lines.Length - 1) * gap;
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            ref var line = ref lines[lineIndex];
            for (int i = line.Start; i < line.End; i++)
            {
                var child = children[i];
                line.Cross = Math.Max(line.Cross, child.Plan.DefiniteContentHeight!.Value + child.Plan.Edges.Vertical
                    + FlexCrossMargins(child, true, plan.ContentWidth));
            }
            natural += line.Cross;
        }
        double available = plan.DefiniteContentHeight ?? natural;
        double remaining = available - natural, offset = 0, spacing = gap, stretch = 0;
        string alignment = style.GetPropertyValue("align-content");
        switch (alignment)
        {
            case "normal": case "stretch": if (remaining > 0 && lines.Length > 0) stretch = remaining / lines.Length; break;
            case "start": case "flex-start": case "flow-start": break;
            case "end": case "flex-end": case "flow-end": offset = remaining; break;
            case "center": offset = remaining / 2; break;
            case "space-between": if (remaining > 0 && lines.Length > 1) spacing += remaining / (lines.Length - 1); break;
            case "space-around":
                if (remaining > 0 && lines.Length > 0) { spacing += remaining / lines.Length; offset = remaining / lines.Length / 2; }
                else offset = remaining / 2;
                break;
            case "space-evenly":
                if (remaining > 0) { double share = remaining / (lines.Length + 1); spacing += share; offset = share; }
                else offset = remaining / 2;
                break;
            default: Fail(e, "align-content", alignment); break;
        }
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i].Cross += stretch; lines[i].Offset = offset;
            offset += lines[i].Cross + spacing;
        }
        return available;
    }
}
