using System;
using System.Globalization;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>CSS2 horizontal-LTR block positioning. Uses the existing computed cascade, not a second CSS parser.
/// Prepare resolves sizes before normal children; Complete consumes their natural content height.
/// Absolute descendants must be laid out after their containing block's final padding box is known.
/// Resolves noncollapsing margins; the flow caller must reject unsupported collapsing contexts.
/// Fixed/sticky positioning, scrollbars and nonzero z-index remain explicit unsupported modes.</summary>
public static class PositionedLayoutSolver
{
    public static PositionedBoxPlan Prepare(CSSStyleDeclaration style, PositionedContainingBlock flowContainingBlock,
        PositionedContainingBlock absoluteContainingBlock, double staticX, double staticY, PositionedIntrinsicWidth intrinsicWidth,
        ImageResource? image = null, AnimatedWidth? animatedWidth = null)
    {
        if (image.HasValue && animatedWidth.HasValue)
            throw new NotSupportedException("Animated width on replaced images is outside the current geometry profile.");
        PositionedMode mode = style.GetPropertyValue("position") switch
        {
            "static" => PositionedMode.Static, "relative" => PositionedMode.Relative, "absolute" => PositionedMode.Absolute,
            var value => throw Reject("position", value)
        };
        if (style.GetPropertyValue("direction") != "ltr") throw Reject("direction", style.GetPropertyValue("direction"));
        if (style.GetPropertyValue("z-index") != "auto") throw Reject("z-index", style.GetPropertyValue("z-index"));
        bool absolute = mode == PositionedMode.Absolute;
        var cb = absolute ? absoluteContainingBlock : flowContainingBlock;
        Rect basis = absolute ? cb.PaddingBox : cb.ContentBox;
        bool definite = absolute || cb.HeightIsDefinite;
        var edges = new PositionedEdges(Length(style, "padding-top", basis.Width), Length(style, "padding-right", basis.Width),
            Length(style, "padding-bottom", basis.Width), Length(style, "padding-left", basis.Width),
            Length(style, "border-top-width", basis.Width), Length(style, "border-right-width", basis.Width),
            Length(style, "border-bottom-width", basis.Width), Length(style, "border-left-width", basis.Width));
        string sizing = style.GetPropertyValue("box-sizing");
        if (sizing is not ("content-box" or "border-box")) throw Reject("box-sizing", sizing);
        bool borderBox = sizing == "border-box";
        double? ml = AutoLength(style, "margin-left", basis.Width), mr = AutoLength(style, "margin-right", basis.Width);
        double? mt = AutoLength(style, "margin-top", basis.Width), mb = AutoLength(style, "margin-bottom", basis.Width);
        double? left = AutoLength(style, "left", basis.Width), right = AutoLength(style, "right", basis.Width);
        double? top = Offset(style, "top", basis.Height, definite), bottom = Offset(style, "bottom", basis.Height, definite);
        ImageSize? imageSize = image.HasValue
            ? ImageLayout.Size(style, image.Value, basis.Width, edges.Horizontal, edges.Vertical, definite ? basis.Height : null)
            : null;
        double? width = imageSize?.Width ?? animatedWidth?.Resolve(basis.Width) ?? AutoLength(style, "width", basis.Width);
        if (width.HasValue && !imageSize.HasValue) width = Math.Max(0, width.Value - (borderBox ? edges.Horizontal : 0));
        if (!width.HasValue)
        {
            if (!absolute) width = Math.Max(0, basis.Width - (ml ?? 0) - (mr ?? 0) - edges.Horizontal);
            else if (left.HasValue && right.HasValue)
                width = Math.Max(0, basis.Width - left.Value - right.Value - (ml ?? 0) - (mr ?? 0) - edges.Horizontal);
            else
            {
                if (!double.IsFinite(intrinsicWidth.Minimum) || !double.IsFinite(intrinsicWidth.Maximum)
                    || intrinsicWidth.Minimum < 0 || intrinsicWidth.Maximum < intrinsicWidth.Minimum)
                    throw new ArgumentOutOfRangeException(nameof(intrinsicWidth));
                double available = basis.Width - (left ?? 0) - (right ?? 0) - (ml ?? 0) - (mr ?? 0) - edges.Horizontal;
                width = Math.Min(Math.Max(intrinsicWidth.Minimum, available), intrinsicWidth.Maximum);
            }
        }
        double minWidth = Constraint(style, "min-width", basis.Width, true, borderBox ? edges.Horizontal : 0, false);
        double maxWidth = Constraint(style, "max-width", basis.Width, true, borderBox ? edges.Horizontal : 0, true);
        width = Constrain(width.Value, minWidth, maxWidth);
        double outerWidth = width.Value + edges.Horizontal;
        double x;
        if (absolute) x = AbsoluteAxis(basis.X, basis.Width, staticX, left, right, outerWidth, ml, mr, true);
        else
        {
            double remaining = basis.Width - outerWidth - (ml ?? 0) - (mr ?? 0);
            double marginLeft = ml ?? (mr == null && remaining >= 0 ? remaining / 2 : Math.Max(0, remaining));
            x = staticX + marginLeft;
            if (mode == PositionedMode.Relative) x += left ?? -(right ?? 0);
        }
        double? height = imageSize?.Height ?? AutoHeight(style, basis.Height, definite);
        if (height.HasValue && !imageSize.HasValue) height = Math.Max(0, height.Value - (borderBox ? edges.Vertical : 0));
        if (absolute && !height.HasValue && top.HasValue && bottom.HasValue)
            height = Math.Max(0, basis.Height - top.Value - bottom.Value - (mt ?? 0) - (mb ?? 0) - edges.Vertical);
        double minHeight = Constraint(style, "min-height", basis.Height, definite, borderBox ? edges.Vertical : 0, false);
        double maxHeight = Constraint(style, "max-height", basis.Height, definite, borderBox ? edges.Vertical : 0, true);
        if (height.HasValue) height = Constrain(height.Value, minHeight, maxHeight);
        double y = staticY + (absolute ? 0 : mt ?? 0);
        if (absolute) y = AbsoluteAxis(basis.Y, basis.Height, staticY, top, bottom, (height ?? 0) + edges.Vertical, mt, mb, false);
        else if (mode == PositionedMode.Relative) y += top ?? -(bottom ?? 0);
        return new PositionedBoxPlan { Mode = mode, ContentWidth = width.Value, DefiniteContentHeight = height, Edges = edges,
            PreLayoutBorderBoxOriginX = x, PreLayoutBorderBoxOriginY = y, Basis = basis, StaticX = staticX, StaticY = staticY,
            MinContentHeight = minHeight, MaxContentHeight = maxHeight,
            Top = top, Bottom = bottom, MarginTop = mt, MarginBottom = mb, FlowMarginTop = mt ?? 0, FlowMarginBottom = mb ?? 0 };
    }

    public static PositionedBoxResult Complete(PositionedBoxPlan plan, double naturalContentHeight)
    {
        if (!double.IsFinite(naturalContentHeight) || naturalContentHeight < 0) throw new ArgumentOutOfRangeException(nameof(naturalContentHeight));
        double height = Constrain(plan.DefiniteContentHeight ?? naturalContentHeight, plan.MinContentHeight, plan.MaxContentHeight);
        var e = plan.Edges;
        double y = plan.Mode == PositionedMode.Absolute
            ? AbsoluteAxis(plan.Basis.Y, plan.Basis.Height, plan.StaticY, plan.Top, plan.Bottom, height + e.Vertical, plan.MarginTop, plan.MarginBottom, false)
            : plan.PreLayoutBorderBoxOriginY;
        var border = new Rect(plan.PreLayoutBorderBoxOriginX, y, plan.ContentWidth + e.Horizontal, height + e.Vertical);
        var content = new Rect(border.X + e.BorderLeft + e.PaddingLeft, border.Y + e.BorderTop + e.PaddingTop, plan.ContentWidth, height);
        var padding = new Rect(border.X + e.BorderLeft, border.Y + e.BorderTop,
            plan.ContentWidth + e.PaddingLeft + e.PaddingRight, height + e.PaddingTop + e.PaddingBottom);
        return new PositionedBoxResult(border, content, padding,
            plan.ExcludedFromFlow ? 0 : border.Height + plan.FlowMarginTop + plan.FlowMarginBottom,
            plan.Mode != PositionedMode.Static, new PositionedContainingBlock(content, padding,
                plan.DescendantHeightIsDefinite ?? plan.DefiniteContentHeight.HasValue));
    }

    public static PositionedPaintContext ResolvePaintContext(CSSStyleDeclaration style, PositionedBoxResult box)
        => ResolvePaintContext(style, box, BoxDecorationResolver.ResolveParts(style, box.BorderBox));
    internal static PositionedPaintContext ResolvePaintContext(CSSStyleDeclaration style, PositionedBoxResult box, in BoxDecorationParts decoration)
    {
        bool x = Clips(style.GetPropertyValue("overflow-x")), y = Clips(style.GetPropertyValue("overflow-y"));
        double innerRadius = 0;
        BorderRadii innerRadii = default;
        if (x || y)
        {
            innerRadii = decoration.Radii.Inner(decoration.Borders).Normalize(box.PaddingBox.Width, box.PaddingBox.Height);
            bool corners = innerRadii.TopLeft.X > 0 && innerRadii.TopLeft.Y > 0
                || innerRadii.TopRight.X > 0 && innerRadii.TopRight.Y > 0
                || innerRadii.BottomRight.X > 0 && innerRadii.BottomRight.Y > 0
                || innerRadii.BottomLeft.X > 0 && innerRadii.BottomLeft.Y > 0;
            if (corners && (!x || !y)) throw Reject("overflow radius", "rounded single-axis clipping unsupported");
            var corner = innerRadii.TopLeft;
            if (corner.X == corner.Y && corner == innerRadii.TopRight && corner == innerRadii.BottomRight && corner == innerRadii.BottomLeft)
                innerRadius = corner.X;
        }
        if (style.GetSpecifiedValue("opacity") is not CssNumberValue opacity
            || !double.IsFinite(opacity.Value) || opacity.Value < 0 || opacity.Value > 1)
            throw Reject("opacity", style.GetPropertyValue("opacity"));
        double alpha = opacity.Value;
        return new PositionedPaintContext(x, y, box.PaddingBox, alpha, innerRadius) { ClipRadii = innerRadii };
    }

    private static bool Clips(string value) => value switch
    {
        "visible" => false, "hidden" or "clip" or "auto" or "scroll" => true, _ => throw Reject("overflow", value)
    };
    private static double AbsoluteAxis(double origin, double available, double staticPosition, double? start, double? end,
        double size, double? marginStart, double? marginEnd, bool horizontal)
    {
        double ms = marginStart ?? 0, me = marginEnd ?? 0;
        if (start.HasValue && end.HasValue)
        {
            double remaining = available - start.Value - end.Value - size - ms - me;
            if (marginStart == null && marginEnd == null) ms = horizontal && remaining < 0 ? 0 : remaining / 2;
            else if (marginStart == null) ms = remaining;
            // Overconstrained LTR horizontal and vertical equations discard right/bottom.
            return origin + start.Value + ms;
        }
        if (start.HasValue) return origin + start.Value + ms;
        if (end.HasValue) return origin + available - end.Value - me - size;
        return staticPosition + ms;
    }
    private static double? AutoHeight(CSSStyleDeclaration style, double basis, bool definite)
    {
        string value = style.GetPropertyValue("height");
        return value == "auto" || value.EndsWith('%') && !definite ? null : Number(value, basis);
    }
    private static double? Offset(CSSStyleDeclaration style, string property, double basis, bool definite)
    {
        string value = style.GetPropertyValue(property);
        return value == "auto" || value.EndsWith('%') && !definite ? null : Number(value, basis);
    }
    private static double Constrain(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(value, maximum));
    private static double Constraint(CSSStyleDeclaration style, string property, double basis, bool definite, double extra, bool maximum)
    {
        string value = style.GetPropertyValue(property);
        if ((!maximum && value == "auto") || (maximum && value == "none") || value.EndsWith('%') && !definite)
            return maximum ? double.PositiveInfinity : 0;
        return Math.Max(0, Number(value, basis) - extra);
    }
    private static double? AutoLength(CSSStyleDeclaration style, string property, double basis)
    {
        string value = style.GetPropertyValue(property);
        return value == "auto" ? null : Number(value, basis);
    }
    private static double Length(CSSStyleDeclaration style, string property, double basis) => Number(style.GetPropertyValue(property), basis);
    private static double Number(string value, double basis)
    {
        if (value == "0") return 0;
        double multiplier;
        ReadOnlySpan<char> number;
        if (value.EndsWith("px", StringComparison.Ordinal)) { number = value.AsSpan(0, value.Length - 2); multiplier = 1; }
        else if (value.EndsWith('%')) { number = value.AsSpan(0, value.Length - 1); multiplier = basis / 100; }
        else throw Reject("length", value);
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw Reject("length", value);
        return result * multiplier;
    }
    private static NotSupportedException Reject(string property, string value) => new($"Positioned layout rejects {property}: {value}");
}
