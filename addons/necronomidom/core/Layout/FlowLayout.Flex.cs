using System;
using System.Buffers;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly record struct FlexChild(Element Element, CSSStyleDeclaration Style, PositionedBoxPlan Plan, string? Text,
        double MinimumWidth, double MaximumWidth, double BaseWidth, double BaseHeight, double ContentHeight);
    private double LayoutFlexChildren(Element e, CSSStyleDeclaration style, PositionedBoxPlan plan, int containing)
        => FlexChildren(e, style, plan, containing, true);

    // Measurement never emits display runs or changes containing/deferred context. The same used sizes
    // are subsequently consumed by Block, so paint, clipping and input retain the existing owner path.
    private double FlexChildrenCore(Element e, CSSStyleDeclaration style, PositionedBoxPlan plan, int containing,
        bool emit, bool measureScrollOverflow, FlexCollectionBuffers buffers)
    {
        string direction = style.GetPropertyValue("flex-direction");
        bool row = direction is "row" or "row-reverse", reverse = direction is "row-reverse" or "column-reverse";
        if (direction is not ("row" or "row-reverse" or "column" or "column-reverse")) Fail(e, "flex-direction", direction);
        string wrapping = style.GetPropertyValue("flex-wrap");
        bool wrap = wrapping == "wrap" && row;
        if (wrapping != "nowrap" && !wrap) Fail(e, "flex-wrap", wrapping);
        string alignment = FlexAlignment(e, style.GetPropertyValue("align-items"));
        var flow = FlexContaining(plan);
        var children = buffers.Children;
        var absolute = buffers.Absolute;
        var text = buffers.Text;
        foreach (var node in e.ChildNodes)
        {
            if (node is Text t) { text.Append(t.Data); continue; }
            if (node is not Element child) continue;
            var cs = _styles.GetComputedStyle(child);
            if (cs.GetPropertyValue("display") == "none") continue;
            if (cs.GetPropertyValue("position") == "absolute")
            {
                if (emit) absolute.Add(child);
                continue;
            }
            AddAnonymous();
            if (cs.GetPropertyValue("order") != "0") Fail(child, "order", cs.GetPropertyValue("order"));
            var intrinsic = FlexIntrinsic(child, cs);
            ImageResource? image = child.LocalName == "img" ? ImageLayout.Intrinsic(_images, child) : null;
            var cp = PositionedLayoutSolver.Prepare(cs, flow, flow, 0, 0, intrinsic, image, SampleWidth(child));
            if (WidthIsAuto(child, cs) && image == null)
                cp = cp with { ContentWidth = Math.Max(0, intrinsic.Maximum) };
            double baseWidth = cp.ContentWidth;
            if (image == null && !WidthIsAuto(child, cs))
                baseWidth = Math.Max(0, UsedWidth(child, cs, plan.ContentWidth)
                    - (cs.GetPropertyValue("box-sizing") == "border-box" ? cp.Edges.Horizontal : 0));
            string self = cs.GetPropertyValue("align-self");
            self = self == "auto" ? alignment : FlexAlignment(child, self);
            if (!row && image == null && WidthIsAuto(child, cs))
            {
                double availableCross = Math.Max(0, plan.ContentWidth - cp.Edges.Horizontal
                    - FlexMargin(cs, "margin-left", plan.ContentWidth) - FlexMargin(cs, "margin-right", plan.ContentWidth));
                bool stretches = self == "stretch" && cs.GetPropertyValue("margin-left") != "auto" && cs.GetPropertyValue("margin-right") != "auto";
                cp = cp with { ContentWidth = stretches ? availableCross : Math.Min(Math.Max(intrinsic.Minimum, availableCross), intrinsic.Maximum) };
            }
            double width = FlexClamp(cs, "width", cp.ContentWidth, cp.Edges.Horizontal, plan.ContentWidth, true, 0);
            cp = cp with { ContentWidth = width };
            double natural = FlexMeasure(child, cs, image != null ? cp : cp with { DefiniteContentHeight = null });
            double baseHeight = cp.DefiniteContentHeight ?? natural;
            if (image == null && !FlexAutoSize(cs, "height", flow.HeightIsDefinite))
                baseHeight = Math.Max(0, Length(cs, "height", plan.DefiniteContentHeight ?? 0)
                    - (cs.GetPropertyValue("box-sizing") == "border-box" ? cp.Edges.Vertical : 0));
            double height = FlexClamp(cs, "height", baseHeight, cp.Edges.Vertical,
                plan.DefiniteContentHeight ?? 0, flow.HeightIsDefinite, 0);
            cp = cp with { DefiniteContentHeight = height };
            children.Add(new FlexChild(child, cs, cp, null, intrinsic.Minimum, intrinsic.Maximum, baseWidth, baseHeight, natural));
        }
        AddAnonymous();
        void AddAnonymous()
        {
            string value = Collapse(text.ToString()); text.Clear();
            if (value.Length == 0) return;
            var intrinsic = FlexTextIntrinsic(e, style, value);
            double width = row ? intrinsic.Maximum : alignment == "stretch" ? plan.ContentWidth
                : Math.Min(Math.Max(intrinsic.Minimum, plan.ContentWidth), intrinsic.Maximum);
            var cp = new PositionedBoxPlan { ContentWidth = width, DefiniteContentHeight = FlexTextHeight(style, value, width), MaxContentHeight = double.PositiveInfinity };
            children.Add(new FlexChild(e, style, cp, value, intrinsic.Minimum, intrinsic.Maximum, cp.ContentWidth, cp.DefiniteContentHeight!.Value, cp.DefiniteContentHeight.Value));
        }
        using var scratch = new FlexScratch(children.Count);
        var main = scratch.Inputs;
        var used = scratch.Results;
        double naturalMain = 0;
        double gap = FlexGap(style, row ? "column-gap" : "row-gap", row ? plan.ContentWidth : flow.HeightIsDefinite ? plan.DefiniteContentHeight : null);
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i]; var cs = child.Style; var cp = child.Plan;
            double decoration = row ? cp.Edges.Horizontal : cp.Edges.Vertical;
            double basis = row ? child.BaseWidth : child.BaseHeight;
            double minimum = child.Text != null ? (row ? child.MinimumWidth : basis) : FlexMinimum(child, row, plan);
            double maximum = child.Text != null ? double.PositiveInfinity : FlexLimit(cs, "max-" + (row ? "width" : "height"),
                decoration, row ? plan.ContentWidth : plan.DefiniteContentHeight ?? 0, row || flow.HeightIsDefinite, double.PositiveInfinity);
            maximum = Math.Max(minimum, maximum);
            double start = 0, end = 0; bool autoStart = false, autoEnd = false;
            if (child.Text == null)
            {
                string basisValue = cs.GetPropertyValue("flex-basis");
                if (basisValue == "content" || basisValue.EndsWith('%') && !row && !flow.HeightIsDefinite)
                    basis = FlexContentBasis(child, row, plan);
                else if (basisValue != "auto")
                    basis = Math.Max(0, Number(basisValue, row ? plan.ContentWidth : plan.DefiniteContentHeight ?? 0)
                        - (cs.GetPropertyValue("box-sizing") == "border-box" ? decoration : 0));
                string startProperty = row ? (reverse ? "margin-right" : "margin-left") : (reverse ? "margin-bottom" : "margin-top");
                string endProperty = row ? (reverse ? "margin-left" : "margin-right") : (reverse ? "margin-top" : "margin-bottom");
                autoStart = cs.GetPropertyValue(startProperty) == "auto"; autoEnd = cs.GetPropertyValue(endProperty) == "auto";
                start = autoStart ? 0 : Length(cs, startProperty, plan.ContentWidth);
                end = autoEnd ? 0 : Length(cs, endProperty, plan.ContentWidth);
            }
            main[i] = new FlexMainItem(FlexLineSolver.Quantize(basis), FlexLineSolver.Quantize(minimum), FlexLineSolver.Quantize(maximum),
                child.Text != null ? 0 : FlexFactor(cs, "flex-grow"), child.Text != null ? 1 : FlexFactor(cs, "flex-shrink"),
                FlexLineSolver.Quantize(start), FlexLineSolver.Quantize(end), autoStart, autoEnd, FlexLineSolver.Quantize(decoration));
            naturalMain += Math.Clamp(main[i].Basis, main[i].Minimum, main[i].Maximum) + main[i].Decoration + main[i].MarginStart + main[i].MarginEnd;
        }
        naturalMain += Math.Max(0, children.Count - 1) * gap;
        double available = FlexLineSolver.Quantize(row ? plan.ContentWidth : plan.DefiniteContentHeight ?? Math.Max(0, naturalMain));
        using var lines = new FlexWrapScratch(wrap ? children.Count : 0);
        int lineCount = wrap ? ResolveWrappedMain(main, used, lines.Values, available, gap, style.GetPropertyValue("justify-content"), reverse) : 0;
        if (!wrap) FlexLineSolver.Resolve(main, used, available, gap, style.GetPropertyValue("justify-content"), reverse);
        double cross = row ? wrap ? 0 : plan.DefiniteContentHeight ?? 0 : plan.ContentWidth;
        // Final main widths change text wrapping and hence the row's natural cross size.
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i]; var cp = child.Plan;
            if (row)
            {
                cp = cp with { ContentWidth = used[i].Size };
                double height;
                if (child.Element.LocalName == "img" && child.Text == null)
                {
                    var image = ImageLayout.Intrinsic(_images, child.Element);
                    height = cp.ContentWidth * image.Height / image.Width;
                }
                else height = child.Text != null ? FlexTextHeight(child.Style, child.Text, cp.ContentWidth)
                    : FlexMeasure(child.Element, child.Style, cp with { DefiniteContentHeight = null });
                bool autoHeight = child.Text != null || FlexAutoSize(child.Style, "height", flow.HeightIsDefinite);
                if (autoHeight) cp = cp with { DefiniteContentHeight = child.Text != null ? height : FlexClamp(child.Style, "height", height, cp.Edges.Vertical,
                    plan.DefiniteContentHeight ?? 0, flow.HeightIsDefinite, 0) };
                if (wrap || !plan.DefiniteContentHeight.HasValue)
                    cross = Math.Max(cross, cp.DefiniteContentHeight!.Value + cp.Edges.Vertical + FlexCrossMargins(child, row, plan.ContentWidth));
            }
            else cp = cp with { DefiniteContentHeight = used[i].Size };
            children[i] = child with { Plan = cp };
        }
        if (wrap) cross = AlignWrappedLines(e, style, plan, children, lines.Values[..lineCount]);
        int lineIndex = 0;
        double scrollBottom = plan.ContentOriginY;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i]; var cs = child.Style; var cp = child.Plan;
            if (wrap && i >= lines.Values[lineIndex].End) lineIndex++;
            double itemCross = wrap ? lines.Values[lineIndex].Cross : cross;
            string self = child.Text != null ? alignment : cs.GetPropertyValue("align-self");
            self = self == "auto" ? alignment : FlexAlignment(child.Element, self);
            string startProperty = row ? "margin-top" : "margin-left", endProperty = row ? "margin-bottom" : "margin-right";
            bool autoStart = child.Text == null && cs.GetPropertyValue(startProperty) == "auto";
            bool autoEnd = child.Text == null && cs.GetPropertyValue(endProperty) == "auto";
            double start = child.Text != null || autoStart ? 0 : Length(cs, startProperty, plan.ContentWidth);
            double end = child.Text != null || autoEnd ? 0 : Length(cs, endProperty, plan.ContentWidth);
            double decoration = row ? cp.Edges.Vertical : cp.Edges.Horizontal;
            double size = row ? cp.DefiniteContentHeight!.Value : cp.ContentWidth;
            if (child.Text != null && !row && self == "stretch") size = itemCross;
            if (self == "stretch" && !autoStart && !autoEnd && child.Text == null
                && (row ? FlexAutoSize(cs, "height", flow.HeightIsDefinite) : WidthIsAuto(child.Element, cs)))
                size = FlexClamp(cs, row ? "height" : "width", Math.Max(0, itemCross - start - end - decoration), decoration,
                    itemCross, row ? flow.HeightIsDefinite : true, 0);
            double remaining = itemCross - size - decoration - start - end;
            double offset = start;
            if (autoStart || autoEnd)
            {
                if (remaining > 0) offset += autoStart ? remaining / (autoEnd ? 2 : 1) : 0;
                else if (autoEnd) offset = start;
            }
            else offset += self switch { "center" => remaining / 2, "flex-end" => remaining, _ => 0 };
            offset = FlexLineSolver.Quantize(offset);
            double x = plan.ContentOriginX + (row ? used[i].Offset : offset);
            double y = plan.ContentOriginY + (row ? offset : used[i].Offset) + (wrap ? lines.Values[lineIndex].Offset : 0);
            // Relative movement is visual only; ordinary block auto-margin centering must not leak into flex placement.
            double relativeX = child.Text == null && cp.Mode == PositionedMode.Relative ? FlexRelative(cs, "left", "right", plan.ContentWidth, true) : 0;
            double relativeY = child.Text == null && cp.Mode == PositionedMode.Relative ? FlexRelative(cs, "top", "bottom", plan.DefiniteContentHeight ?? 0, flow.HeightIsDefinite) : 0;
            cp = cp with { ContentWidth = row ? cp.ContentWidth : size, DefiniteContentHeight = row ? size : cp.DefiniteContentHeight,
                PreLayoutBorderBoxOriginX = x + relativeX, PreLayoutBorderBoxOriginY = y + relativeY,
                StaticX = x, StaticY = y, FlowMarginTop = 0, FlowMarginBottom = 0,
                DescendantHeightIsDefinite = child.Text == null && FlexUsedHeightIsDefinite(cs, row, flow.HeightIsDefinite, self, autoStart || autoEnd) };
            if (measureScrollOverflow)
                scrollBottom = Math.Max(scrollBottom, cp.PreLayoutBorderBoxOriginY + cp.DefiniteContentHeight.GetValueOrDefault() + cp.Edges.Vertical);
            if (emit)
            {
                if (child.Text != null) Inline(child.Element, cs, child.Text, x, y, cp.ContentWidth);
                else Block(child.Element, x, y, flow, containing, usedPlan: cp);
            }
        }
        if (emit && absolute.Count != 0)
        {
            double height = row ? cross : available;
            var finalFlow = FlexContaining(plan with { DefiniteContentHeight = height });
            foreach (var child in absolute)
            {
                var cs = _styles.GetComputedStyle(child);
                var intrinsic = FlexIntrinsic(child, cs);
                ImageResource? image = child.LocalName == "img" ? ImageLayout.Intrinsic(_images, child) : null;
                var ap = PositionedLayoutSolver.Prepare(cs, finalFlow, finalFlow, 0, 0, intrinsic, image, SampleWidth(child));
                double ah = ap.DefiniteContentHeight ?? FlexMeasure(child, cs, ap);
                double aw = ap.ContentWidth + ap.Edges.Horizontal; ah += ap.Edges.Vertical;
                string self = cs.GetPropertyValue("align-self");
                self = self == "auto" ? alignment : FlexAlignment(child, self);
                double mainSpace = (row ? plan.ContentWidth : height) - (row ? aw : ah);
                double crossSpace = (row ? height : plan.ContentWidth) - (row ? ah : aw);
                double mo = style.GetPropertyValue("justify-content") switch
                { "center" or "space-around" or "space-evenly" => mainSpace / 2, "flex-end" or "flow-end" or "end" => mainSpace, _ => 0 };
                if (reverse) mo = mainSpace - mo;
                double co = self switch { "center" => crossSpace / 2, "flex-end" => crossSpace, _ => 0 };
                DeferAbsolute(child, finalFlow, plan.ContentOriginX + (row ? mo : co), plan.ContentOriginY + (row ? co : mo), containing);
            }
        }
        double heightResult = row ? cross : available;
        return measureScrollOverflow ? Math.Max(heightResult, scrollBottom - plan.ContentOriginY) : heightResult;
    }

    private readonly struct FlexScratch : IDisposable
    {
        private readonly FlexMainItem[] _inputs;
        private readonly FlexMainResult[] _results;
        private readonly int _count;
        public FlexScratch(int count)
        {
            _count = count;
            _inputs = count == 0 ? Array.Empty<FlexMainItem>() : ArrayPool<FlexMainItem>.Shared.Rent(count);
            _results = count == 0 ? Array.Empty<FlexMainResult>() : ArrayPool<FlexMainResult>.Shared.Rent(count);
        }
        public Span<FlexMainItem> Inputs => _inputs.AsSpan(0, _count);
        public Span<FlexMainResult> Results => _results.AsSpan(0, _count);
        public void Dispose()
        {
            if (_count == 0) return;
            ArrayPool<FlexMainItem>.Shared.Return(_inputs);
            ArrayPool<FlexMainResult>.Shared.Return(_results);
        }
    }

}
