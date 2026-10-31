using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly record struct ContainingContext(Element? Element, PositionedContainingBlock? Box, int First = -1, int Last = -1);
    private readonly record struct DeferredBlock(Element Element, PositionedContainingBlock Flow, double X, double Y, int Next = -1, bool Processed = false);
    private readonly List<ContainingContext> _containing = new();
    private readonly List<DeferredBlock> _deferred = new();
    private Rect _viewport;

    private double LayoutChildren(Element e, CSSStyleDeclaration s, PositionedBoxPlan plan, int containing)
    {
        if (e is HTMLTextAreaElement || e is HTMLInputElement { IsTextEditable: true })
            return LayoutTextControl((HTMLElement)e, s, plan);
        if (s.GetPropertyValue("display") == "flex") return LayoutFlexChildren(e, s, plan, containing);
        var content = new Rect(plan.ContentOriginX, plan.ContentOriginY, plan.ContentWidth, plan.DefiniteContentHeight ?? 0);
        var edges = plan.Edges;
        var padding = new Rect(content.X - edges.PaddingLeft, content.Y - edges.PaddingTop,
            content.Width + edges.PaddingLeft + edges.PaddingRight, content.Height + edges.PaddingTop + edges.PaddingBottom);
        var flow = new PositionedContainingBlock(content, padding, plan.DescendantHeightIsDefinite ?? plan.DefiniteContentHeight.HasValue);
        if (HasInlineChildren(e))
        {
            foreach (var child in e.ChildNodes)
                if (child is Element absolute && _styles.GetComputedStyle(absolute).GetPropertyValue("position") == "absolute")
                    DeferAbsolute(absolute, flow, content.X, content.Y, containing);
            return LayoutInlineFragments(e, s, content.X, content.Y, content.Width, true);
        }
        double y = content.Y;
        var text = new StringBuilder(); bool blocks = false;
        foreach (var child in e.ChildNodes)
        {
            if (child is Text t) text.Append(t.Data);
            else if (child is Element element)
            {
                var style = _styles.GetComputedStyle(element);
                string display = style.GetPropertyValue("display");
                if (display == "none") continue;
                if (style.GetPropertyValue("position") == "absolute")
                {
                    DeferAbsolute(element, flow, content.X, y, containing);
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(text.ToString())) Fail(e, "children", "mixed block and inline");
                text.Clear(); blocks = true;
                y += Block(element, content.X, y, flow, containing);
            }
        }
        string collapsed = Collapse(text.ToString());
        if (blocks && collapsed.Length != 0) Fail(e, "children", "mixed block and inline");
        if (collapsed.Length != 0) y += Inline(e, s, collapsed, content.X, y, content.Width);
        return y - content.Y;
    }
    private void ValidateDeferredContexts(Element element, int containing)
    {
        // Overflow escapes follow the positioning containing block, but opacity/clip-path groups
        // remain DOM subtree effects. Non-laminar static effect groups require a richer paint graph.
        for (var ancestor = element.ParentElement; ancestor != null && ancestor != _containing[containing].Element; ancestor = ancestor.ParentElement)
        {
            if (ancestor.ParentElement == null) break; // Initial containing block is painted within the root context.
            var style = _styles.GetComputedStyle(ancestor);
            // Six-digit CSSOM text can serialize a real near-one effect as "1"; keep its typed boundary.
            var computedOpacity = style.GetSpecifiedValue("opacity");
            if (computedOpacity is not CssNumberValue)
                Fail(ancestor, "opacity", "computed numeric opacity required");
            if (((CssNumberValue)computedOpacity!).Value < 1 || style.GetPropertyValue("clip-path") != "none")
                Fail(element, "positioned paint", "absolute descendant crosses a static opacity/clip-path group");
        }
    }
    private void DeferAbsolute(Element element, PositionedContainingBlock flow, double staticX, double staticY, int containing)
    {
        ValidateDeferredContexts(element, containing);
        int index = _deferred.Count;
        var context = _containing[containing];
        if (context.Last >= 0) _deferred[context.Last] = _deferred[context.Last] with { Next = index };
        _containing[containing] = context with { First = context.First < 0 ? index : context.First, Last = index };
        _deferred.Add(new DeferredBlock(element, flow, staticX, staticY));
    }
    private void LayoutDeferred(int containing)
    {
        var context = _containing[containing];
        if (context.First < 0) return;
        var box = context.Box ?? throw new InvalidOperationException("Absolute containing block has not completed layout.");
        _containing[containing] = context with { First = -1, Last = -1 };
        for (int i = context.First; i >= 0; i = _deferred[i].Next)
        {
            var item = _deferred[i];
            _deferred[i] = item with { Processed = true };
            Block(item.Element, item.X, item.Y, item.Flow, containing, box);
        }
    }
    private PositionedIntrinsicWidth IntrinsicWidth(Element e, CSSStyleDeclaration style)
    {
        if (style.GetPropertyValue("position") != "absolute" || !WidthIsAuto(e, style)
            || style.GetPropertyValue("left") != "auto" && style.GetPropertyValue("right") != "auto") return default;
        foreach (var child in e.ChildNodes)
        {
            if (child is Element ce)
            {
                var cs = _styles.GetComputedStyle(ce);
                if (cs.GetPropertyValue("display") == "none" || cs.GetPropertyValue("position") == "absolute") continue;
                if (WidthHasPercentage(ce, cs)) Fail(ce, "intrinsic width", "percentage child width in shrink-to-fit formatting is unsupported");
            }
        }
        // Reuse the actual recursive block/flex/replaced-content intrinsic law, including box edges and margins.
        return FlexIntrinsic(e, style);
    }
    private PositionedIntrinsicWidth MeasureIntrinsicText(Element e, CSSStyleDeclaration style, string text)
    {
        if (text.Length == 0) return default;
        ValidateText(e, text);
        int size = TextFontSize(style);
        var shaping = TextShaping(style, size);
        double maximum = shaping.Shaper.Measure(text, size, shaping.Weight, shaping.Spacing, shaping.Italic).Width, minimum = 0;
        if (style.GetPropertyValue("text-wrap-mode") == "nowrap") return new PositionedIntrinsicWidth(maximum, maximum);
        if (System.Text.Ascii.IsValid(text))
            foreach (string word in text.Split(' ')) minimum = Math.Max(minimum, shaping.Shaper.Measure(word, size, shaping.Weight, shaping.Spacing, shaping.Italic).Width);
        else
        {
            int cursor = 0;
            while (NextUnicodeChunk(text, ref cursor, out int start, out int end))
                minimum = Math.Max(minimum, shaping.Shaper.Measure(text[start..end], size, shaping.Weight, shaping.Spacing, shaping.Italic).Width);
        }
        return new PositionedIntrinsicWidth(minimum, maximum);
    }
}
