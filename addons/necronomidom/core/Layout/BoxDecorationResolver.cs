using NecronomiDom.Css.Om;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

internal readonly record struct BoxDecorationParts(Rgba Background, BorderSides Borders, BorderRadii Radii);

public static class BoxDecorationResolver
{
    public static BorderBoxRun Resolve(CSSStyleDeclaration style, Rect finalBorderBounds)
    {
        var parts = ResolveParts(BoxStyleSnapshot.For(style), finalBorderBounds);
        return new(finalBorderBounds, parts.Background, parts.Borders, parts.Radii);
    }
    /// <summary>Border sides and background come from the snapshot's per-version cache; only the
    /// bounds-dependent radii are resolved per box.</summary>
    internal static BoxDecorationParts ResolveParts(BoxStyleSnapshot style, Rect finalBorderBounds)
    {
        ref readonly BorderSides borders = ref style.Borders;
        var radii = style.Radii(finalBorderBounds).Normalize(finalBorderBounds.Width, finalBorderBounds.Height);
        return new(style.Background, borders, radii);
    }
}
