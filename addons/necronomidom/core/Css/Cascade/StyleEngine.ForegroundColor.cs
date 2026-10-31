using System;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade;

internal readonly record struct ForegroundColorDependencies(ComputedValueColor Target, Element? Parent,
    bool Inherited, CssValue? Expression, CssValue? Stroke, CssValue? Shadow);

public sealed partial class StyleEngine
{
    // Provenance comes from the existing winning cascade/substitution and composed inheritance parent.
    // Public computed declarations remain unchanged; no second cascade or RGB-equality inference.
    internal ForegroundColorDependencies GetForegroundColorDependencies(Element element)
    {
        Entry entry = GetEntry(element);
        if (!entry.Values.TryGetValue("color", out CssValue? color) || color is not ComputedValueColor target)
            throw new NotSupportedException("Runtime requires computed foreground color.");
        return new(target, entry.ForegroundParent, entry.ForegroundInherited, entry.ForegroundExpression,
            entry.InheritedStrokeColor, entry.InheritedTextShadow);
    }
}
