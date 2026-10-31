using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>Used caret (<c>caret-color</c>) and selection (<c>--necronomidom-selection-color</c>) colors, resolved
/// against the control foreground.</summary>
public static class TextControlColors
{
    // The box shader interpolates border/fill at the edge even at border-width zero.
    // Keep both sides the same straight color/alpha; transparent black would
    // darken and attenuate a thin caret twice.
    public static BoxRun Swatch(Rect bounds, Rgba color) => new(bounds, new BoxStyle(color, color, 0, 0));

    public static (Rgba Caret, Rgba Selection) Resolve(CSSStyleDeclaration style, Rgba foreground)
    {
        CssValue? caret = null, selection = null;
        foreach (var declaration in style.EnumerateLonghands())
        {
            if (declaration.Name == "caret-color") caret = declaration.Value;
            else if (declaration.Name == SelectionColorProperty) selection = declaration.Value;
        }
        return (Color(caret, "caret-color", foreground, foreground),
            Color(selection, SelectionColorProperty, new Rgba(0.2f, 0.4f, 0.8f, 1), foreground));
    }

    /// <summary>Custom property that sets the selection highlight of input/textarea text.</summary>
    public const string SelectionColorProperty = "--necronomidom-selection-color";

    private static Rgba Color(CssValue? value, string property, Rgba fallback, Rgba foreground)
    {
        if (value == null || value is CssKeywordValue { Value: "auto" }) return fallback;
        if (value is CssUnparsedValue tokens)
        {
            if (!CssColorParser.TryParse(tokens.Tokens, out var parsed))
                throw new NotSupportedException("Text-control color must parse: " + property);
            value = parsed;
        }
        // Consume the color AST, not just ComputedValueColor: custom properties retain
        // tokens, and computed colors may retain a currentcolor-dependent expression.
        if (value is not CssColorValue color || !color.TryResolve(new CssColorContext
            { CurrentColor = new CssResolvedColor("srgb", foreground.R, foreground.G, foreground.B, foreground.A) }, out var resolved))
            throw new NotSupportedException("Text-control color must resolve: " + property + "=" + value?.Serialize());
        var rgb = resolved.ToSrgb();
        return new Rgba((float)rgb.C1, (float)rgb.C2, (float)rgb.C3, (float)rgb.Alpha);
    }
}
