using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private Rect CompleteScrollGeometry(Element element, CSSStyleDeclaration style, PositionedBoxResult box,
        double natural, int boxIndex, int runStart, int contextStart, PaintPhaseFrame frame, ScrollGutters gutters)
    {
        double right = box.PaddingBox.X + box.PaddingBox.Width;
        double paddingBottom = box.PaddingBox.Y + box.PaddingBox.Height - box.ContentBox.Y - box.ContentBox.Height;
        double bottom = Math.Max(box.PaddingBox.Y + box.PaddingBox.Height, box.ContentBox.Y + natural + paddingBottom);
        double paddingRight = box.PaddingBox.X + box.PaddingBox.Width - box.ContentBox.X - box.ContentBox.Width;
        for (int i = boxIndex + 1; i < _result.Boxes.Count; i++)
        {
            var hit = _result.Boxes[i];
            if (!ReferenceEquals(hit.Element.ParentElement, element)) continue;
            var extent = hit.ScrollOverflow;
            right = Math.Max(right, Math.Max(hit.Bounds.X + hit.Bounds.Width, extent.X + extent.Width) + paddingRight);
            bottom = Math.Max(bottom, Math.Max(hit.Bounds.Y + hit.Bounds.Height, extent.Y + extent.Height) + paddingBottom);
        }
        // Leaf text can overflow horizontally even when its flow box is constrained.
        if (element.ChildElementCount == 0)
            for (int i = runStart; i < _result.Runs.Count; i++)
            {
                TextRun? text = _result.Runs[i] as TextRun ?? (_result.Runs[i] as TextEffectsRun)?.Text;
                if (text != null) right = Math.Max(right, text.X + _fontFamilies[text.FontFamily].Measure(text.Text, text.FontSize, text.FontWeight, text.LetterSpacing, text.FontItalic).Width + paddingRight);
            }
        string x = style.GetPropertyValue("overflow-x"), y = style.GetPropertyValue("overflow-y");
        bool scrollX = x is "auto" or "scroll" or "hidden", scrollY = y is "auto" or "scroll" or "hidden";
        if (scrollX || scrollY || element.ScrollState != null)
        {
            var state = element.ScrollState ??= new ElementScrollState();
            state.X = scrollX; state.Y = scrollY;
            state.UserX = x is "auto" or "scroll"; state.UserY = y is "auto" or "scroll";
            state.ClientWidth = box.PaddingBox.Width; state.ClientHeight = box.PaddingBox.Height;
            state.GutterRight = gutters.Right; state.GutterBottom = gutters.Bottom;
            state.Width = Math.Max(state.ClientWidth, right - box.PaddingBox.X);
            state.Height = Math.Max(state.ClientHeight, bottom - box.PaddingBox.Y);
            state.Set(state.Left, state.Top);
            state.LayoutLeft = state.Left; state.LayoutTop = state.Top;
            if (state.Left != 0 || state.Top != 0)
            {
                TranslateSubtree(boxIndex + 1, runStart, contextStart, _deferred.Count, -state.Left, -state.Top);
                TranslateHeldPaint(frame, -state.Left, -state.Top);
            }
        }
        // Only visible overflow propagates into an ancestor's scrollable area.
        return new Rect(box.PaddingBox.X, box.PaddingBox.Y,
            x == "visible" ? right - box.PaddingBox.X : box.PaddingBox.Width,
            y == "visible" ? bottom - box.PaddingBox.Y : box.PaddingBox.Height);
    }
}
