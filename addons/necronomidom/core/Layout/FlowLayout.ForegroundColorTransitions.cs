using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly IAnimatedForegroundColorSource? _animatedForegroundColor;
    private readonly Dictionary<Element, Rgba>? _foregroundPaint;

    private Rgba SampleForegroundColor(Element element, CSSStyleDeclaration style) =>
        _animatedForegroundColor == null ? Color(style, "color") : SampleForegroundColor(element);

    private Rgba SampleForegroundColor(Element element)
    {
        if (_foregroundPaint!.TryGetValue(element, out Rgba current)) return current;
        if (!_animatedForegroundColor!.TryGetForegroundColor(element, out current))
        {
            var dependencies = _styles.GetForegroundColorDependencies(element);
            Rgba parent = dependencies.Parent == null ? default : SampleForegroundColor(dependencies.Parent);
            current = ForegroundColorTransitionStyle.PaintColor(in dependencies, parent);
        }
        _foregroundPaint[element] = current;
        return current;
    }

    private TextEffectsStyle SampleForegroundEffects(Element element, Rgba color)
    {
        var values = _styles.GetComputedValues(element);
        if (_animatedForegroundColor == null) return TextEffectsResolver.Resolve(values);
        var dependencies = _styles.GetForegroundColorDependencies(element);
        return TextEffectsResolver.Resolve(values, dependencies.Stroke, dependencies.Shadow, color);
    }
}
