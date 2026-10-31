using System.Collections.Generic;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Css.Cascade;

public sealed partial class StyleEngine
{
    // Public declarations remain resolved typed colors. Only these inherited text properties retain
    // a currentcolor-dependent computed AST for the next element: copying the resolved parent RGB
    // loses the dependency, while copying the original specified shadow loses computed font units.
    private static CssValue? InheritedValue(string name, Entry? parent)
    {
        if (parent == null) return null;
        return name switch
        {
            "-webkit-text-stroke-color" => parent.InheritedStrokeColor ?? parent.Values.GetValueOrDefault(name),
            "text-shadow" => parent.InheritedTextShadow ?? parent.Values.GetValueOrDefault(name),
            _ => parent.Values.GetValueOrDefault(name)
        };
    }
    private static CssValue ComputeTextColor(string name, CssValue specified, Entry entry, in ComputedValueContext context)
    {
        if (name is not ("-webkit-text-stroke-color" or "text-shadow"))
            return ComputedValueResolver.Resolve(name, specified, context);
        // Existing normalization computes lengths and literal colors first, but leaves currentcolor
        // typed when its basis is unavailable. Binding it for this element does not replace that AST.
        CssValue inherited = ComputedValueResolver.Resolve(name, specified, context with { CurrentColor = null });
        if (!UsesCurrentColor(inherited)) return inherited;
        if (name == "text-shadow") entry.InheritedTextShadow = inherited;
        else entry.InheritedStrokeColor = inherited;
        return ComputedValueResolver.Resolve(name, inherited, context);
    }
    private static bool UsesCurrentColor(CssValue value)
    {
        if (value is CssColorValue color) return color.RequiresCurrentColor;
        if (value is CssListValue list)
            foreach (CssValue item in list.Items)
                if (UsesCurrentColor(item)) return true;
        return false;
    }
}
