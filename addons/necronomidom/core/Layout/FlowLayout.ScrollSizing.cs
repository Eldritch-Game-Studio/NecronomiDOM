using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly record struct ScrollGutters(double Right, double Bottom)
    {
        internal PositionedBoxPlan Children(PositionedBoxPlan plan) => plan with {
            ContentWidth = Math.Max(0, plan.ContentWidth - Right),
            DefiniteContentHeight = plan.DefiniteContentHeight is { } height ? Math.Max(0, height - Bottom) : null,
            MinContentHeight = Math.Max(0, plan.MinContentHeight - Bottom),
            MaxContentHeight = Math.Max(0, plan.MaxContentHeight - Bottom)
        };
        internal PositionedBoxResult Complete(PositionedBoxResult box)
        {
            if (Right == 0 && Bottom == 0) return box;
            var content = box.ContentBox with { Width = Math.Max(0, box.ContentBox.Width - Right), Height = Math.Max(0, box.ContentBox.Height - Bottom) };
            var padding = box.PaddingBox with { Width = Math.Max(0, box.PaddingBox.Width - Right), Height = Math.Max(0, box.PaddingBox.Height - Bottom) };
            return box with { ContentBox = content, PaddingBox = padding,
                DescendantContainingBlock = new PositionedContainingBlock(content, padding, box.DescendantContainingBlock.HeightIsDefinite) };
        }
    }
    private ScrollGutters ResolveScrollGutters(Element element, CSSStyleDeclaration style, PositionedBoxPlan plan)
    {
        string x = style.GetPropertyValue("overflow-x"), y = style.GetPropertyValue("overflow-y");
        if (x is not ("auto" or "scroll") && y is not ("auto" or "scroll")) return default;
        double size = style.GetPropertyValue("scrollbar-width") switch { "none" => 0, "thin" => 8, _ => 15 };
        size = ScrollbarLength(style, "--necronomidom-scrollbar-size", size);
        if (style.GetPropertyValue("scrollbar-width") == "none" || size <= 0) return default;
        var gutters = new ScrollGutters(y == "scroll" ? size : 0, x == "scroll" ? size : 0);
        // Each axis can enable once; the third pass settles the cross-axis dependency.
        for (int pass = 0; pass < 3; pass++)
        {
            var children = gutters.Children(plan);
            double natural = style.GetPropertyValue("display") == "flex"
                ? FlexChildren(element, style, children, 0, false, measureScrollOverflow: true)
                : FlexMeasure(element, style, children);
            var final = PositionedLayoutSolver.Complete(plan, natural + gutters.Bottom);
            double right = gutters.Right, bottom = gutters.Bottom;
            if (y == "auto" && natural > Math.Max(0, final.ContentBox.Height - bottom)) right = size;
            if (x == "auto" && FlexIntrinsic(element, style).Minimum > children.ContentWidth) bottom = size;
            var next = new ScrollGutters(Math.Min(right, final.PaddingBox.Width), Math.Min(bottom, final.PaddingBox.Height));
            if (next == gutters) return gutters;
            gutters = next;
        }
        return gutters;
    }
}
