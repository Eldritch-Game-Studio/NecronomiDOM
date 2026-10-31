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
    private static void ValidateText(Element element, string text)
    {
        if (!UnicodeLtrText.IsAdmitted(text)) Fail(element, "text", "requires supported LTR text and collapsed spaces; RTL/bidi controls are unavailable");
    }
    private static int TextFontSize(CSSStyleDeclaration style)
    {
        double size = Number(style.GetPropertyValue("font-size"), 0);
        if (size != Math.Truncate(size) || size < 1 || size > 256)
            throw new NotSupportedException("Runtime font-size requires integer px in [1,256].");
        return (int)size;
    }
    private readonly record struct TextShapingParameters(double Weight, double Spacing, string Family, ITextShaper Shaper, bool Italic);
    private (string Family, ITextShaper Shaper) ResolveTextFamily(CSSStyleDeclaration style)
    {
        string family = AuthoredFontFamilies.Name(style.GetSpecifiedValue("font-family"));
        if (!_fontFamilies.TryGetValue(family, out var shaper))
            throw new NotSupportedException("Runtime font-family is not authored/mapped: " + family + "; fallback is disabled.");
        return (family, shaper);
    }
    private TextShapingParameters TextShaping(CSSStyleDeclaration style, int size)
    {
        if (style.GetSpecifiedValue("font-weight") is not CssNumberValue weight
            || !double.IsFinite(weight.Value) || weight.Value is < 1 or > 1000)
            throw new NotSupportedException("Runtime requires a computed numeric font-weight in [1,1000].");
        var value = style.GetSpecifiedValue("letter-spacing")
            ?? throw new NotSupportedException("Missing computed letter-spacing.");
        double spacing = value is CssKeywordValue { Value: "normal" } ? 0 : UsedCssNumeric.Length(value, size);
        var (family, shaper) = ResolveTextFamily(style);
        return new TextShapingParameters(weight.Value, spacing, family, shaper, style.GetPropertyValue("font-style") == "italic");
    }
    private double TextLineHeight(CSSStyleDeclaration style, int size, TextShapingParameters shaping)
    {
        string height = style.GetPropertyValue("line-height");
        if (height != "normal") return Number(height, 0);
        // A font strut, not an arbitrary multiplier or a glyph-dependent text bounding box.
        var metrics = shaping.Shaper.Measure(" ", size, shaping.Weight, shaping.Spacing, shaping.Italic);
        return metrics.Ascent + metrics.Descent + metrics.LineGap;
    }
    private double Inline(Element element, CSSStyleDeclaration style, string text, double x, double y, double width)
    {
        ValidateText(element, text);
        int size = TextFontSize(style);
        var shaping = TextShaping(style, size);
        double height = TextLineHeight(style, size, shaping);
        string align = style.GetPropertyValue("text-align");
        var color = SampleForegroundColor(element, style);
        var effects = SampleForegroundEffects(element, color);
        bool ellipsis = style.GetPropertyValue("text-overflow") == "ellipsis"
            && style.GetPropertyValue("overflow-x") != "visible";
        if (style.GetPropertyValue("text-wrap-mode") == "nowrap")
        {
            PaintTextLine(element, text, x, y, width, height, size, align, color, ellipsis, effects, shaping);
            return height;
        }
        var lines = new List<string>();
        if (!Ascii.IsValid(text)) WrapUnicodeText(text, size, shaping, width, lines);
        else
        {
            string line = "";
            foreach (string word in text.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length != 0 && shaping.Shaper.Measure(candidate, size, shaping.Weight, shaping.Spacing, shaping.Italic).Width > width) { lines.Add(line); line = word; }
                else line = candidate;
            }
            if (line.Length != 0) lines.Add(line);
        }
        foreach (string value in lines)
        {
            PaintTextLine(element, value, x, y, width, height, size, align, color, ellipsis, effects, shaping);
            y += height;
        }
        return lines.Count * height;
    }
    private void PaintTextLine(Element owner, string text, double x, double y, double width, double height, int size,
        string align, Rgba color, bool ellipsis, TextEffectsStyle effects, TextShapingParameters shaping)
    {
        var metrics = shaping.Shaper.Measure(text, size, shaping.Weight, shaping.Spacing, shaping.Italic);
        double offset = Math.Max(0, align switch { "center" => (width - metrics.Width) / 2, "right" or "end" => width - metrics.Width, _ => 0 });
        double baseline = y + (height - metrics.Ascent - metrics.Descent) / 2 + metrics.Ascent;
        if (!ellipsis || metrics.Width <= width)
        {
            PaintTextGlyphs(owner, text, x + offset, baseline, size, color, effects, shaping);
            return;
        }
        if (width <= 0) return;
        var marker = shaping.Shaper.Measure("\u2026", size, shaping.Weight, shaping.Spacing, shaping.Italic);
        double available = Math.Max(0, width - marker.Width - offset);
        int low = 0, high = text.Length;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (shaping.Shaper.Measure(text[..middle], size, shaping.Weight, shaping.Spacing, shaping.Italic).Width <= available) low = middle;
            else high = middle - 1;
        }
        // CSS Overflow preserves the first character: clip it rather than replacing it.
        if (low == 0) low = 1;
        string prefix = text[..low];
        double markerX = offset + shaping.Shaper.Measure(prefix, size, shaping.Weight, shaping.Spacing, shaping.Italic).Width;
        double top = baseline - Math.Max(metrics.Ascent, marker.Ascent);
        double bottom = baseline + Math.Max(metrics.Descent, marker.Descent);
        _result.Runs.Add(new BeginRectClipRun(new Rect(x, top, width, bottom - top)));
        PaintTextGlyphs(owner, prefix, x + offset, baseline, size, color, effects, shaping);
        PaintTextGlyphs(owner, "\u2026", x + markerX, baseline, size, color, effects, shaping);
        _result.Runs.Add(new EndRectClipRun());
    }
    private void PaintTextGlyphs(Element owner, string text, double x, double baseline, int size, Rgba color, TextEffectsStyle effects,
        TextShapingParameters shaping)
    {
        var run = new TextRun(text, x, baseline, size, color) { Owner = owner, FontWeight = shaping.Weight, LetterSpacing = shaping.Spacing, FontFamily = shaping.Family, FontItalic = shaping.Italic };
        _result.Runs.Add(effects.HasEffects ? new TextEffectsRun(run, effects) : run);
    }
}
