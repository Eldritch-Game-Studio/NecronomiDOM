using System;
using System.Collections.Generic;
using System.Globalization;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly record struct ControlLine(string Text, int Offset, bool ParagraphEnd);
    private readonly List<ControlLine> _controlLines = new();
    private double LayoutTextControl(HTMLElement element, CSSStyleDeclaration style, PositionedBoxPlan plan)
    {
        string value = ((ITextControl)element).Value;
        int size = TextFontSize(style);
        var shaping = TextShaping(style, size);
        double height = TextLineHeight(style, size, shaping);
        bool wrap = element is HTMLTextAreaElement && element.GetAttribute("wrap") != "off" && style.GetPropertyValue("text-wrap-mode") != "nowrap";
        var lines = ControlLines(value, size, shaping, wrap ? plan.ContentWidth : double.PositiveInfinity,
            style.GetPropertyValue("overflow-wrap") == "break-word");
        double contentHeight = plan.DefiniteContentHeight ?? height * lines.Count;
        _result.Runs.Add(new BeginRectClipRun(new Rect(plan.ContentOriginX, plan.ContentOriginY, plan.ContentWidth, contentHeight)));
        double y = plan.ContentOriginY;
        foreach (var line in lines)
        {
            string text = line.Text;
            if (element is HTMLInputElement { Type: "password" }) text = new string('\u2022', text.Length);
            var metrics = shaping.Shaper.Measure(text, size, shaping.Weight, shaping.Spacing, shaping.Italic);
            double x = plan.ContentOriginX + Math.Max(0, style.GetPropertyValue("text-align") switch
                { "center" => (plan.ContentWidth - metrics.Width) / 2, "right" or "end" => plan.ContentWidth - metrics.Width, _ => 0 });
            double baseline = y + (height - metrics.Ascent - metrics.Descent) / 2 + metrics.Ascent;
            _result.Runs.Add(new TextRun(text, x, baseline, size, SampleForegroundColor(element, style))
            {
                Owner = element, FontWeight = shaping.Weight, LetterSpacing = shaping.Spacing, FontFamily = shaping.Family, FontItalic = shaping.Italic, TextControl = element, TextOffset = line.Offset,
                TextControlLineEnd = line.ParagraphEnd, ControlLine = new Rect(plan.ContentOriginX, y, plan.ContentWidth, height),
                TextControlViewport = new Rect(plan.ContentOriginX, plan.ContentOriginY, plan.ContentWidth, contentHeight)
            });
            y += height;
        }
        _result.Runs.Add(new EndRectClipRun());
        return lines.Count * height;
    }
    private List<ControlLine> ControlLines(string value, int size, TextShapingParameters shaping, double width, bool breakWords)
    {
        var result = _controlLines; result.Clear();
        int offset = 0;
        do
        {
            int end = value.IndexOf('\n', offset);
            if (end < 0) end = value.Length;
            string paragraph = value[offset..end];
            while (paragraph.Length > 0 && width > 0 && shaping.Shaper.Measure(paragraph, size, shaping.Weight, shaping.Spacing, shaping.Italic).Width > width)
            {
                int[] boundaries = StringInfo.ParseCombiningCharacters(paragraph);
                int low = 1, high = boundaries.Length;
                while (low < high)
                {
                    int middle = low + (high - low + 1) / 2;
                    int length = middle == boundaries.Length ? paragraph.Length : boundaries[middle];
                    if (shaping.Shaper.Measure(paragraph[..length], size, shaping.Weight, shaping.Spacing, shaping.Italic).Width <= width) low = middle;
                    else high = middle - 1;
                }
                int fit = low == boundaries.Length ? paragraph.Length : boundaries[low];
                if (fit == paragraph.Length) break;
                int space = paragraph.LastIndexOf(' ', fit - 1, fit);
                if (space >= 0) fit = space + 1;
                else if (!breakWords)
                { int next = paragraph.IndexOf(' ', fit); if (next < 0) break; fit = next + 1; }
                result.Add(new ControlLine(paragraph[..fit], offset, false));
                offset += fit; paragraph = paragraph[fit..];
            }
            result.Add(new ControlLine(paragraph, offset, true));
            offset = end + 1;
        } while (offset <= value.Length);
        return result;
    }
    private double TextControlHeight(HTMLElement element, CSSStyleDeclaration style, double width)
    {
        int size = TextFontSize(style); var shaping = TextShaping(style, size);
        bool wrap = element is HTMLTextAreaElement && element.GetAttribute("wrap") != "off"
            && style.GetPropertyValue("text-wrap-mode") != "nowrap";
        int count = ControlLines(((ITextControl)element).Value, size, shaping, wrap ? width : double.PositiveInfinity,
            style.GetPropertyValue("overflow-wrap") == "break-word").Count;
        return count * TextLineHeight(style, size, shaping);
    }
    private PositionedIntrinsicWidth TextControlIntrinsic(ITextControl control, CSSStyleDeclaration style)
    {
        int size = TextFontSize(style); var shaping = TextShaping(style, size);
        double width = 0;
        foreach (var line in ControlLines(control.Value, size, shaping, double.PositiveInfinity, false))
            width = Math.Max(width, shaping.Shaper.Measure(line.Text, size, shaping.Weight, shaping.Spacing, shaping.Italic).Width);
        return new PositionedIntrinsicWidth(control is HTMLTextAreaElement ? 0 : width, width);
    }
}
