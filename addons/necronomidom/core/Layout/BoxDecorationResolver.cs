using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

internal readonly record struct BoxDecorationParts(Rgba Background, BorderSides Borders, BorderRadii Radii);

public static class BoxDecorationResolver
{
    public static BorderBoxRun Resolve(CSSStyleDeclaration style, Rect finalBorderBounds)
    {
        var parts = ResolveParts(style, finalBorderBounds);
        return new(finalBorderBounds, parts.Background, parts.Borders, parts.Radii);
    }
    internal static BoxDecorationParts ResolveParts(CSSStyleDeclaration style, Rect finalBorderBounds)
    {
        var borders = new BorderSides(
            Side(style, "border-top-width", "border-top-color", "border-top-style"),
            Side(style, "border-right-width", "border-right-color", "border-right-style"),
            Side(style, "border-bottom-width", "border-bottom-color", "border-bottom-style"),
            Side(style, "border-left-width", "border-left-color", "border-left-style"));
        var radii = new BorderRadii(Corner(style, "border-top-left-radius", finalBorderBounds), Corner(style, "border-top-right-radius", finalBorderBounds),
            Corner(style, "border-bottom-right-radius", finalBorderBounds), Corner(style, "border-bottom-left-radius", finalBorderBounds));
        return new(Color(style, "background-color"), borders, radii.Normalize(finalBorderBounds.Width, finalBorderBounds.Height));
    }
    private static BorderSide Side(CSSStyleDeclaration style, string widthProperty, string colorProperty, string styleProperty)
    {
        string lineStyle = (style.GetSpecifiedValue(styleProperty) as CssKeywordValue)?.Value
            ?? throw new NotSupportedException("Runtime requires computed border style: " + styleProperty);
        if (lineStyle is not ("none" or "hidden" or "solid"))
            throw new NotSupportedException("Runtime border style is unavailable: " + lineStyle);
        double width = lineStyle is "none" or "hidden" ? 0 : UsedCssNumeric.Length(Required(style, widthProperty), 0);
        if (width < 0) throw new NotSupportedException("Negative used border width.");
        return new(width, Color(style, colorProperty), lineStyle);
    }
    private static BorderCorner Corner(CSSStyleDeclaration style, string property, Rect bounds)
    {
        CssValue value = Required(style, property);
        CssValue x = value, y = value;
        if (value is CssListValue list)
        {
            if (list.Items.Count != 2) throw new NotSupportedException("Unresolved border corner radius.");
            x = list.Items[0]; y = list.Items[1];
        }
        return new(Math.Max(0, UsedCssNumeric.Length(x, bounds.Width)), Math.Max(0, UsedCssNumeric.Length(y, bounds.Height)));
    }
    private static CssValue Required(CSSStyleDeclaration style, string property) => style.GetSpecifiedValue(property)
        ?? throw new NotSupportedException("Missing computed property: " + property);
    private static Rgba Color(CSSStyleDeclaration style, string property)
    {
        if (Required(style, property) is not ComputedValueColor color)
            throw new NotSupportedException("Runtime requires resolved color: " + property);
        var rgb = color.Color.ToSrgb();
        return new((float)Math.Clamp(rgb.C1, 0, 1), (float)Math.Clamp(rgb.C2, 0, 1), (float)Math.Clamp(rgb.C3, 0, 1), (float)Math.Clamp(rgb.Alpha, 0, 1));
    }
}
