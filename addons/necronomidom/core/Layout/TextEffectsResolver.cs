using System;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>Reads computed CSS values directly, without serialization/reparsing precision loss.</summary>
public static class TextEffectsResolver
{
    public static TextEffectsStyle Resolve(CSSStyleDeclaration computedValues)
        => ResolvePaint(computedValues, null, null, null);

    internal static TextEffectsStyle Resolve(CSSStyleDeclaration computedValues, CssValue? stroke,
        CssValue? shadow, Rgba foreground) => ResolvePaint(computedValues, stroke, shadow, foreground);

    private static TextEffectsStyle ResolvePaint(CSSStyleDeclaration computedValues, CssValue? stroke,
        CssValue? shadow, Rgba? foreground)
    {
        double width = UsedCssNumeric.Length(Required(computedValues, "-webkit-text-stroke-width"), 0);
        Rgba strokeColor = Color(stroke ?? Required(computedValues, "-webkit-text-stroke-color"), foreground);
        CssValue value = shadow ?? Required(computedValues, "text-shadow");
        if (value is CssKeywordValue { Value: "none" }) return new(width, strokeColor, default);
        if (value is CssListValue { Separator: CssListSeparator.Comma } layers)
        {
            var shadows = new TextShadow[layers.Items.Count];
            for (int i = 0; i < shadows.Length; i++) shadows[i] = Shadow(layers.Items[i], foreground);
            return new(width, strokeColor, shadows);
        }
        return new(width, strokeColor, new[] { Shadow(value, foreground) });
    }
    private static TextShadow Shadow(CssValue value, Rgba? foreground)
    {
        // The existing computed-value resolver canonicalizes each text-shadow to color/x/y/blur.
        if (value is not CssListValue { Separator: CssListSeparator.Space, Items.Count: 4 } layer)
            throw new NotSupportedException("Runtime requires computed color/x/y/blur text-shadow values.");
        return new(UsedCssNumeric.Length(layer.Items[1], 0), UsedCssNumeric.Length(layer.Items[2], 0),
            UsedCssNumeric.Length(layer.Items[3], 0), Color(layer.Items[0], foreground));
    }
    private static CssValue Required(CSSStyleDeclaration style, string property) => style.GetSpecifiedValue(property)
        ?? throw new NotSupportedException("Missing computed property: " + property);
    private static Rgba Color(CssValue value, Rgba? foreground)
    {
        CssResolvedColor resolved;
        if (foreground.HasValue && value is CssAbsoluteColorValue { Keyword: "currentcolor" })
            resolved = new("srgb", foreground.Value.R, foreground.Value.G, foreground.Value.B, foreground.Value.A);
        else if (value is ComputedValueColor color) resolved = color.Color;
        else if (foreground.HasValue && value is CssColorValue { RequiresCurrentColor: true } dependent
            && dependent.TryResolve(new CssColorContext
                { CurrentColor = new("srgb", foreground.Value.R, foreground.Value.G, foreground.Value.B, foreground.Value.A) }, out resolved)) { }
        else throw new NotSupportedException("Runtime requires resolved text effect colors.");
        var rgb = resolved.ToSrgb();
        return new((float)Math.Clamp(rgb.C1, 0, 1), (float)Math.Clamp(rgb.C2, 0, 1),
            (float)Math.Clamp(rgb.C3, 0, 1), (float)Math.Clamp(rgb.Alpha, 0, 1));
    }
}
