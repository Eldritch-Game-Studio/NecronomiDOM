using System;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private double MeasureFlexHeight(Element e, BoxStyleSnapshot style, PositionedBoxPlan plan)
    {
        if (e.LocalName == "img") return plan.DefiniteContentHeight ?? 0;
        if (e is HTMLTextAreaElement || e is HTMLInputElement { IsTextEditable: true })
            return TextControlHeight((HTMLElement)e, style.Style, plan.ContentWidth);
        if (style.Display == "flex") return FlexChildren(e, style, plan, 0, false);
        if (HasInlineChildren(e)) return LayoutInlineFragments(e, style.Style, 0, 0, plan.ContentWidth, false);
        double height = 0;
        // Children recurse into the same measurement, so the element's text is collected in its own rented builder.
        CollectedText? text = null; bool blocks = false;
        var flow = FlexContaining(plan);
        for (var node = e.FirstChild; node != null; node = node.NextSibling)
        {
            if (node is Text t) (text ??= RentText()).Append(t.Data);
            else if (node is Element child)
            {
                var cs = BoxStyleSnapshot.For(_styles.GetComputedStyle(child));
                if (cs.DisplayNone || cs.Mode == PositionedMode.Absolute) continue;
                if (text != null && !text.IsWhiteSpace()) Fail(e, "children", "mixed block and inline");
                text?.Clear(); blocks = true;
                ImageResource? image = child.LocalName == "img" ? ImageLayout.Intrinsic(_images, child) : null;
                var cp = PositionedLayoutSolver.Prepare(cs, flow, flow, 0, 0, FlexIntrinsic(child, cs), image, SampleWidth(child));
                double natural = FlexMeasure(child, cs, cp);
                height += FlexClamp(cs, false, cp.DefiniteContentHeight ?? natural, cp.Edges.Vertical,
                    plan.DefiniteContentHeight ?? 0, flow.HeightIsDefinite, 0) + cp.Edges.Vertical
                    + (cp.MarginTop ?? 0) + (cp.MarginBottom ?? 0);
            }
        }
        string collapsed = text == null ? "" : text.Collapse();
        if (text != null) ReturnText(text);
        if (blocks && collapsed.Length != 0) Fail(e, "children", "mixed block and inline");
        return height + (collapsed.Length == 0 ? 0 : FlexTextHeight(style, collapsed, plan.ContentWidth));
    }
    private PositionedIntrinsicWidth MeasureFlexIntrinsic(Element e, BoxStyleSnapshot style)
    {
        if (e is HTMLTextAreaElement || e is HTMLInputElement { IsTextEditable: true })
            return TextControlIntrinsic((ITextControl)e, style.Style);
        if (e.LocalName == "img")
        {
            _ = SampleWidth(e);
            var image = ImageLayout.Intrinsic(_images, e);
            double horizontal = Length(style, style.PaddingLeft, "padding-left", 0) + Length(style, style.PaddingRight, "padding-right", 0)
                + Length(style, style.BorderLeftWidth, "border-left-width", 0) + Length(style, style.BorderRightWidth, "border-right-width", 0);
            double vertical = Length(style, style.PaddingTop, "padding-top", 0) + Length(style, style.PaddingBottom, "padding-bottom", 0)
                + Length(style, style.BorderTopWidth, "border-top-width", 0) + Length(style, style.BorderBottomWidth, "border-bottom-width", 0);
            if (style.Width.IsPercent)
            {
                var height = style.Height;
                double width = height.IsAuto || height.IsPercent ? image.Width
                    : Math.Max(0, Length(style, height, "height", 0) - (style.BorderBox ? vertical : 0)) * image.Width / image.Height;
                return new PositionedIntrinsicWidth(width, width);
            }
            var size = ImageLayout.Size(style.Style, image, 0, horizontal, vertical, null);
            return new PositionedIntrinsicWidth(size.Width, size.Width);
        }
        if (style.Display != "flex" && HasInlineChildren(e))
            return InlineIntrinsic(e, style.Style);
        double minimum = 0, maximum = 0;
        bool row = style.Display == "flex" && style.FlexDirection is "row" or "row-reverse";
        bool wrap = row && style.FlexWrap == "wrap";
        int count = 0;
        // Children recurse into the same intrinsic measurement, so the element's text is collected locally.
        CollectedText? text = null;
        void AddText()
        {
            if (text == null || text.Length == 0) return;
            string value = text.Collapse(); text.Clear();
            if (value.Length == 0) return;
            var own = FlexTextIntrinsic(e, style.Style, value);
            if (style.TextWrapMode == "nowrap") own = new PositionedIntrinsicWidth(own.Maximum, own.Maximum);
            if (row) { minimum = wrap ? Math.Max(minimum, own.Minimum) : minimum + own.Minimum; maximum += own.Maximum; }
            else { minimum = Math.Max(minimum, own.Minimum); maximum = Math.Max(maximum, own.Maximum); }
            count++;
        }
        for (var node = e.FirstChild; node != null; node = node.NextSibling)
        {
            if (node is Text t) (text ??= RentText()).Append(t.Data);
            else if (node is Element child)
            {
                var cs = BoxStyleSnapshot.For(_styles.GetComputedStyle(child));
                if (cs.DisplayNone || cs.Mode == PositionedMode.Absolute) continue;
                AddText(); count++;
                var nested = FlexIntrinsic(child, cs);
                double extra = Length(cs, cs.PaddingLeft, "padding-left", 0) + Length(cs, cs.PaddingRight, "padding-right", 0)
                    + Length(cs, cs.BorderLeftWidth, "border-left-width", 0) + Length(cs, cs.BorderRightWidth, "border-right-width", 0);
                double min = nested.Minimum + extra, max = nested.Maximum + extra;
                if (IntrinsicSpecifiedWidth(child, cs) is { } specifiedWidth)
                    min = max = FlexLineSolver.Quantize(specifiedWidth) + (cs.BorderBox ? 0 : extra);
                double margins = FlexMargin(cs, cs.MarginLeft, "margin-left", 0) + FlexMargin(cs, cs.MarginRight, "margin-right", 0);
                if (row) { if (wrap) minimum = Math.Max(minimum, min + margins); else minimum += min + margins; maximum += max + margins; }
                else { minimum = Math.Max(minimum, min + margins); maximum = Math.Max(maximum, max + margins); }
            }
        }
        AddText();
        if (text != null) ReturnText(text);
        if (row)
        {
            double gap = FlexGap(style, style.ColumnGap, "column-gap", null) * Math.Max(0, count - 1);
            if (!wrap) minimum += gap;
            maximum += gap;
        }
        return new PositionedIntrinsicWidth(minimum, maximum);
    }
    private PositionedIntrinsicWidth FlexTextIntrinsic(Element element, Css.Om.CSSStyleDeclaration style, string text)
    {
        // InlineNode::ComputeMinMaxSizes consumes ShapeResult::SnappedWidth (ceil), not raw glyph advances.
        var size = MeasureIntrinsicText(element, style, text);
        return new(FlexLineSolver.CeilIntrinsic(size.Minimum), FlexLineSolver.CeilIntrinsic(size.Maximum));
    }
    private double FlexTextHeight(BoxStyleSnapshot style, string text, double width)
    {
        int fontSize = TextFontSize(style.Style);
        var shaping = TextShaping(style.Style, fontSize);
        double lineHeight = TextLineHeight(style.Style, fontSize, shaping);
        if (text.Length == 0) return 0;
        if (style.TextWrapMode == "nowrap") return lineHeight;
        if (!System.Text.Ascii.IsValid(text)) return WrapUnicodeText(text, fontSize, shaping, width) * lineHeight;
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
        var style = child.Style;
        BoxLength cross = row ? style.Height : style.Width;
        string property = row ? "height" : "width";
        bool definite = !row || FlexHeightBasis(parent);
        double decoration = row ? child.Plan.Edges.Vertical : child.Plan.Edges.Horizontal;
        double basis = row ? parent.DefiniteContentHeight ?? 0 : parent.ContentWidth;
        double size = FlexAutoSize(cross, definite) ? row ? image.Height : image.Width
            : Math.Max(0, Length(style, cross, property, basis) - (style.BorderBox ? decoration : 0));
        size = FlexClamp(style, !row, size, decoration, basis, definite, 0);
        return row ? size * image.Width / image.Height : size * image.Height / image.Width;
    }
    private static bool FlexHeightBasis(PositionedBoxPlan plan)
        => plan.DescendantHeightIsDefinite ?? plan.DefiniteContentHeight.HasValue;
    private static bool FlexUsedHeightIsDefinite(BoxStyleSnapshot style, bool row, bool parentDefinite, string alignment, bool autoCrossMargin)
    {
        if (row)
            return !FlexAutoSize(style.Height, parentDefinite) || parentDefinite && alignment == "stretch" && !autoCrossMargin;
        if (parentDefinite) return true;
        var basis = style.FlexBasis;
        return basis.IsAuto ? !FlexAutoSize(style.Height, false) : basis.Kind != BoxLengthKind.Content && !basis.IsPercent;
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
        var cs = child.Style;
        double decoration = row ? child.Plan.Edges.Horizontal : child.Plan.Edges.Vertical;
        double basis = row ? parent.ContentWidth : parent.DefiniteContentHeight ?? 0;
        bool definite = row || FlexHeightBasis(parent);
        double automatic = (row ? cs.OverflowX : cs.OverflowY) is "hidden" or "scroll" or "auto" ? 0
            : child.Element.LocalName == "img" ? FlexContentBasis(child, row, parent) : row ? child.MinimumWidth : child.ContentHeight;
        if (row && !WidthIsAuto(child.Element, cs))
            automatic = Math.Min(automatic, Math.Max(0, UsedWidth(child.Element, cs, basis) - (cs.BorderBox ? decoration : 0)));
        else if (!row && !cs.Height.IsAuto && !(cs.Height.IsPercent && !definite))
            automatic = Math.Min(automatic, Math.Max(0, Length(cs, cs.Height, "height", basis) - (cs.BorderBox ? decoration : 0)));
        automatic = Math.Min(automatic, FlexLimit(cs, row ? cs.MaxWidth : cs.MaxHeight, row ? "max-width" : "max-height", decoration, basis, definite, double.PositiveInfinity));
        return FlexLimit(cs, row ? cs.MinWidth : cs.MinHeight, row ? "min-width" : "min-height", decoration, basis, definite, automatic);
    }
    /// <summary>Clamps a used size between the min-/max- longhands of one axis (<paramref name="width"/> selects the axis).</summary>
    private static double FlexClamp(BoxStyleSnapshot style, bool width, double size, double decoration, double basis, bool definite, double automatic)
    {
        double minimum = FlexLimit(style, width ? style.MinWidth : style.MinHeight, width ? "min-width" : "min-height", decoration, basis, definite, automatic);
        double maximum = Math.Max(minimum, FlexLimit(style, width ? style.MaxWidth : style.MaxHeight, width ? "max-width" : "max-height", decoration, basis, definite, double.PositiveInfinity));
        return FlexLineSolver.Quantize(Math.Clamp(size, minimum, maximum));
    }
    private static double FlexLimit(BoxStyleSnapshot style, in BoxLength value, string property, double decoration, double basis, bool definite, double fallback)
    {
        if (value.IsAuto || value.IsNone || value.IsPercent && !definite) return fallback;
        return Math.Max(0, FlexLineSolver.Quantize(Length(style, value, property, basis)) - (style.BorderBox ? decoration : 0));
    }
    private static bool FlexAutoSize(in BoxLength value, bool definite) => value.IsAuto || value.IsPercent && !definite;
    private static double FlexRelative(BoxStyleSnapshot style, in BoxLength a, in BoxLength b, string start, string end, double basis, bool definite)
    {
        if (!a.IsAuto && !(a.IsPercent && !definite)) return Length(style, a, start, basis);
        return b.IsAuto || b.IsPercent && !definite ? 0 : -Length(style, b, end, basis);
    }
    private static double FlexMargin(BoxStyleSnapshot style, in BoxLength value, string property, double basis)
        => value.IsAuto ? 0 : FlexLineSolver.Quantize(Length(style, value, property, basis));
    private static double FlexCrossMargins(FlexChild child, bool row, double basis) => child.Text != null ? 0
        : row ? FlexMargin(child.Style, child.Style.MarginTop, "margin-top", basis) + FlexMargin(child.Style, child.Style.MarginBottom, "margin-bottom", basis)
        : FlexMargin(child.Style, child.Style.MarginLeft, "margin-left", basis) + FlexMargin(child.Style, child.Style.MarginRight, "margin-right", basis);
    private static double FlexGap(BoxStyleSnapshot style, in BoxLength value, string property, double? basis)
        => value.Kind == BoxLengthKind.Normal || value.IsPercent && !basis.HasValue ? 0 : FlexLineSolver.Quantize(Length(style, value, property, basis ?? 0));
    private static double FlexFactor(BoxStyleSnapshot style, double value, string property)
        => double.IsNaN(value) ? throw new NotSupportedException("Flex " + property + ": " + style.Style.GetPropertyValue(property)) : value;
    private static string FlexAlignment(Element e, string value)
    {
        if (value is "normal" or "stretch") return "stretch";
        if (value == "flow-start") return "flex-start";
        if (value == "flow-end") return "flex-end";
        if (value is "center" or "flex-start" or "flex-end") return value;
        Fail(e, "flex alignment", value); return "";
    }
}
