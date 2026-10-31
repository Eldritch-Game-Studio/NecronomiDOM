using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly IAnimatedBackgroundColorSource? _animatedBackgroundColor;
    private Rgba SampleBackgroundColor(Element element, Rgba target) =>
        _animatedBackgroundColor != null && _animatedBackgroundColor.TryGetBackgroundColor(element, out Rgba current)
            ? current : target;
}
