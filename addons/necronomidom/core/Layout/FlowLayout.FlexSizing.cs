using System;
using System.Globalization;
using System.Text;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private double MeasureFlexHeight(Element e, CSSStyleDeclaration style, PositionedBoxPlan plan)
    {
        if (e.LocalName == "img") return plan.DefiniteContentHeight ?? 0;
        if (e is HTMLTextAreaElement || e is HTMLInputElement { IsTextEditable: true })
            return TextControlHeight((HTMLElement)e, style, plan.ContentWidth);
        if (style.GetPropertyValue("display") == "flex") return FlexChildren(e, style, plan, 0, false);
        if (HasInlineChildren(e)) return LayoutInlineFragments(e, style, 0, 0, plan.ContentWidth, false);
        double height = 0;
        var text = new StringBuilder(); bool blocks = false;
        var flow = FlexContaining(plan);
        foreach (var node in e.ChildNodes)
        {
            if (node is Text t) text.Append(t.Data);
            else if (node is Element child)
            {
                var cs = _styles.GetComputedStyle(child);
                if (cs.GetPropertyValue("display") == "none" || cs.GetPropertyValue("position") == "absolute") continue;
                if (!string.IsNullOrWhiteSpace(text.ToString())) Fail(e, "children", "mixed block and inline");
                text.Clear(); blocks = true;
                ImageResource? image = child.LocalName == "img" ? ImageLayout.Intrinsic(_images, child) : null;
                var cp = PositionedLayoutSolver.Prepare(cs, flow, flow, 0, 0, FlexIntrinsic(child, cs), image, SampleWidth(child));
                double natural = FlexMeasure(child, cs, cp);
                height += FlexClamp(cs, "height", cp.DefiniteContentHeight ?? natural, cp.Edges.Vertical,
                    plan.DefiniteContentHeight ?? 0, flow.HeightIsDefinite, 0) + cp.Edges.Vertical
                    + (cp.MarginTop ?? 0) + (cp.MarginBottom ?? 0);
            }
        }
        string collapsed = Collapse(text.ToString());
        if (blocks && collapsed.Length != 0) Fail(e, "children", "mixed block and inline");
        return height + (collapsed.Length == 0 ? 0 : FlexTextHeight(style, collapsed, plan.ContentWidth));
    }
    private PositionedIntrinsicWidth MeasureFlexIntrinsic(Element e, CSSStyleDeclaration style)
    {
        if (e is HTMLTextAreaElement || e is HTMLInputElement { IsTextEditable: true })
            return TextControlIntrinsic((ITextControl)e, style);
        if (e.LocalName == "img")
        {
            _ = SampleWidth(e);
            var image = ImageLayout.Intrinsic(_images, e);
            double horizontal = Length(style, "padding-left", 0) + Length(style, "padding-right", 0)
                + Length(style, "border-left-width", 0) + Length(style, "border-right-width", 0);
            double vertical = Length(style, "padding-top", 0) + Length(style, "padding-bottom", 0)
                + Length(style, "border-top-width", 0) + Length(style, "border-bottom-width", 0);
            if (style.GetPropertyValue("width").EndsWith('%'))
            {
                string height = style.GetPropertyValue("height");
                double width = height == "auto" || height.EndsWith('%') ? image.Width
                    : Math.Max(0, Number(height, 0) - (style.GetPropertyValue("box-sizing") == "border-box" ? vertical : 0)) * image.Width / image.Height;
                return new PositionedIntrinsicWidth(width, width);
            }
            var size = ImageLayout.Size(style, image, 0, horizontal, vertical, null);
            return new PositionedIntrinsicWidth(size.Width, size.Width);
        }
        if (style.GetPropertyValue("display") != "flex" && HasInlineChildren(e))
            return InlineIntrinsic(e, style);
        double minimum = 0, maximum = 0;
        var text = new StringBuilder();
        bool row = style.GetPropertyValue("display") == "flex" && style.GetPropertyValue("flex-direction") is "row" or "row-reverse";
        bool wrap = row && style.GetPropertyValue("flex-wrap") == "wrap";
        int count = 0;
        void AddText()
        {
            string value = Collapse(text.ToString()); text.Clear();
            if (value.Length == 0) return;
            var own = FlexTextIntrinsic(e, style, value);
            if (style.GetPropertyValue("text-wrap-mode") == "nowrap") own = new PositionedIntrinsicWidth(own.Maximum, own.Maximum);
            if (row) { minimum = wrap ? Math.Max(minimum, own.Minimum) : minimum + own.Minimum; maximum += own.Maximum; }
            else { minimum = Math.Max(minimum, own.Minimum); maximum = Math.Max(maximum, own.Maximum); }
            count++;
        }
        foreach (var node in e.ChildNodes)
        {
            if (node is Text t) text.Append(t.Data);
            else if (node is Element child)
            {
                var cs = _styles.GetComputedStyle(child);
                if (cs.GetPropertyValue("display") == "none" || cs.GetPropertyValue("position") == "absolute") continue;
                AddText(); count++;
                var nested = FlexIntrinsic(child, cs);
                double extra = Length(cs, "padding-left", 0) + Length(cs, "padding-right", 0)
                    + Length(cs, "border-left-width", 0) + Length(cs, "border-right-width", 0);
                double min = nested.Minimum + extra, max = nested.Maximum + extra;
                if (IntrinsicSpecifiedWidth(child, cs) is { } specifiedWidth)
                    min = max = FlexLineSolver.Quantize(specifiedWidth) + (cs.GetPropertyValue("box-sizing") == "border-box" ? 0 : extra);
                double margins = FlexMargin(cs, "margin-left", 0) + FlexMargin(cs, "margin-right", 0);
                if (row) { if (wrap) minimum = Math.Max(minimum, min + margins); else minimum += min + margins; maximum += max + margins; }
                else { minimum = Math.Max(minimum, min + margins); maximum = Math.Max(maximum, max + margins); }
            }
        }
        AddText();
        if (row)
        {
            double gap = FlexGap(style, "column-gap", null) * Math.Max(0, count - 1);
            if (!wrap) minimum += gap;
            maximum += gap;
        }
        return new PositionedIntrinsicWidth(minimum, maximum);
    }
    private PositionedIntrinsicWidth FlexTextIntrinsic(Element element, CSSStyleDeclaration style, string text)
    {
        // InlineNode::ComputeMinMaxSizes consumes ShapeResult::SnappedWidth (ceil), not raw glyph advances.
        var size = MeasureIntrinsicText(element, style, text);
        return new(FlexLineSolver.CeilIntrinsic(size.Minimum), FlexLineSolver.CeilIntrinsic(size.Maximum));
    }
    private double FlexTextHeight(CSSStyleDeclaration style, string text, double width)
    {
        int fontSize = TextFontSize(style);
        var shaping = TextShaping(style, fontSize);
        double lineHeight = TextLineHeight(style, fontSize, shaping);
        if (text.Length == 0) return 0;
        if (style.GetPropertyValue("text-wrap-mode") == "nowrap") return lineHeight;
        if (!Ascii.IsValid(text)) return WrapUnicodeText(text, fontSize, shaping, width) * lineHeight;
        int lines = 1; string line = "";
        foreach (string word in text.Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length != 0 && shaping.Shaper.Measure(candidate, fontSize, shaping.Weight, shaping.Spacing, shaping.Italic).Width > width) { lines++; line = word; }
            else line = candidate;
        }
        return lines * lineHeight;
    }
    private double FlexContentBasis(FlexChild child, bool row, PositionedBoxPlan parent)
    {
        if (child.Element.LocalName != "img")
            return row ? child.MaximumWidth : child.ContentHeight;
        var image = ImageLayout.Intrinsic(_images, child.Element);
        string cross = row ? "height" : "width";
        bool definite = !row || FlexHeightBasis(parent);
        double decoration = row ? child.Plan.Edges.Vertical : child.Plan.Edges.Horizontal;
        double basis = row ? parent.DefiniteContentHeight ?? 0 : parent.ContentWidth;
        double size = FlexAutoSize(child.Style, cross, definite) ? row ? image.Height : image.Width
            : Math.Max(0, Length(child.Style, cross, basis) - (child.Style.GetPropertyValue("box-sizing") == "border-box" ? decoration : 0));
        size = FlexClamp(child.Style, cross, size, decoration, basis, definite, 0);
        return row ? size * image.Width / image.Height : size * image.Height / image.Width;
    }
    private static bool FlexHeightBasis(PositionedBoxPlan plan)
        => plan.DescendantHeightIsDefinite ?? plan.DefiniteContentHeight.HasValue;
    private static bool FlexUsedHeightIsDefinite(CSSStyleDeclaration style, bool row, bool parentDefinite, string alignment, bool autoCrossMargin)
    {
        if (row)
            return !FlexAutoSize(style, "height", parentDefinite) || parentDefinite && alignment == "stretch" && !autoCrossMargin;
        if (parentDefinite) return true;
        string basis = style.GetPropertyValue("flex-basis");
        return basis == "auto" ? !FlexAutoSize(style, "height", false) : basis != "content" && !basis.EndsWith('%');
    }
    private static PositionedContainingBlock FlexContaining(PositionedBoxPlan plan)
    {
        var edges = plan.Edges;
        var content = new Rect(plan.ContentOriginX, plan.ContentOriginY, plan.ContentWidth, plan.DefiniteContentHeight ?? 0);
        return new PositionedContainingBlock(content, new Rect(content.X - edges.PaddingLeft, content.Y - edges.PaddingTop,
            content.Width + edges.PaddingLeft + edges.PaddingRight, content.Height + edges.PaddingTop + edges.PaddingBottom), FlexHeightBasis(plan));
    }
    private double FlexMinimum(FlexChild child, bool row, PositionedBoxPlan parent)
    {
        var cs = child.Style; string property = row ? "width" : "height";
        double decoration = row ? child.Plan.Edges.Horizontal : child.Plan.Edges.Vertical;
        double basis = row ? parent.ContentWidth : parent.DefiniteContentHeight ?? 0;
        bool definite = row || FlexHeightBasis(parent);
        double automatic = cs.GetPropertyValue(row ? "overflow-x" : "overflow-y") is "hidden" or "scroll" or "auto" ? 0
            : child.Element.LocalName == "img" ? FlexContentBasis(child, row, parent) : row ? child.MinimumWidth : child.ContentHeight;
        string specified = cs.GetPropertyValue(property);
        if (row && !WidthIsAuto(child.Element, cs))
            automatic = Math.Min(automatic, Math.Max(0, UsedWidth(child.Element, cs, basis)
                - (cs.GetPropertyValue("box-sizing") == "border-box" ? decoration : 0)));
        else if (!row && specified != "auto" && !(specified.EndsWith('%') && !definite))
            automatic = Math.Min(automatic, Math.Max(0, Number(specified, basis) - (cs.GetPropertyValue("box-sizing") == "border-box" ? decoration : 0)));
        automatic = Math.Min(automatic, FlexLimit(cs, "max-" + property, decoration, basis, definite, double.PositiveInfinity));
        return FlexLimit(cs, "min-" + property, decoration, basis, definite, automatic);
    }
    private static double FlexClamp(CSSStyleDeclaration style, string dimension, double size, double decoration, double basis, bool definite, double automatic)
    {
        double minimum = FlexLimit(style, "min-" + dimension, decoration, basis, definite, automatic);
        double maximum = Math.Max(minimum, FlexLimit(style, "max-" + dimension, decoration, basis, definite, double.PositiveInfinity));
        return FlexLineSolver.Quantize(Math.Clamp(size, minimum, maximum));
    }
    private static double FlexLimit(CSSStyleDeclaration style, string property, double decoration, double basis, bool definite, double fallback)
    {
        string value = style.GetPropertyValue(property);
        if (value is "auto" or "none" || value.EndsWith('%') && !definite) return fallback;
        return Math.Max(0, FlexLineSolver.Quantize(Number(value, basis)) - (style.GetPropertyValue("box-sizing") == "border-box" ? decoration : 0));
    }
    private static bool FlexAutoSize(CSSStyleDeclaration style, string dimension, bool definite)
    {
        string value = style.GetPropertyValue(dimension);
        return value == "auto" || value.EndsWith('%') && !definite;
    }
    private static double FlexRelative(CSSStyleDeclaration style, string start, string end, double basis, bool definite)
    {
        string a = style.GetPropertyValue(start), b = style.GetPropertyValue(end);
        if (a != "auto" && !(a.EndsWith('%') && !definite)) return Number(a, basis);
        return b == "auto" || b.EndsWith('%') && !definite ? 0 : -Number(b, basis);
    }
    private static double FlexMargin(CSSStyleDeclaration style, string property, double basis)
        => style.GetPropertyValue(property) == "auto" ? 0 : FlexLineSolver.Quantize(Length(style, property, basis));
    private static double FlexCrossMargins(FlexChild child, bool row, double basis) => child.Text != null ? 0
        : FlexMargin(child.Style, row ? "margin-top" : "margin-left", basis) + FlexMargin(child.Style, row ? "margin-bottom" : "margin-right", basis);
    private static double FlexGap(CSSStyleDeclaration style, string property, double? basis)
    {
        string value = style.GetPropertyValue(property);
        return value == "normal" || value.EndsWith('%') && !basis.HasValue ? 0 : FlexLineSolver.Quantize(Number(value, basis ?? 0));
    }
    private static double FlexFactor(CSSStyleDeclaration style, string property)
    {
        if (!double.TryParse(style.GetPropertyValue(property), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < 0)
            throw new NotSupportedException("Flex " + property + ": " + style.GetPropertyValue(property));
        return value;
    }
    private static string FlexAlignment(Element e, string value)
    {
        if (value is "normal" or "stretch") return "stretch";
        if (value == "flow-start") return "flex-start";
        if (value == "flow-end") return "flex-end";
        if (value is "center" or "flex-start" or "flex-end") return value;
        Fail(e, "flex alignment", value); return "";
    }
}
