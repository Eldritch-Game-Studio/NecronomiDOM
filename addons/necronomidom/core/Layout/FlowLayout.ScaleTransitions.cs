using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly IAnimatedScaleSource? _animatedScale;
    private AnimatedScale? SampleScale(Element element)
        => _animatedScale != null && _animatedScale.TryGetScale(element, out var scale) ? scale : null;
}
