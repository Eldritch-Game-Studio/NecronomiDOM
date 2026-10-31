using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly IAnimatedOpacitySource? _animatedOpacity;
    private readonly Dictionary<PaintRun, Element> _opacityOwners = new(ReferenceEqualityComparer.Instance);

    /// <summary>Identifies the owner of any actual emitted opacity group, not an equal bounds/value record.</summary>
    public bool TryGetOpacityOwner(PaintRun run, out Element owner) => _opacityOwners.TryGetValue(run, out owner!);

    private PositionedPaintContext SamplePaintOpacity(Element element, PositionedPaintContext context) =>
        _animatedOpacity != null && _animatedOpacity.TryGetOpacity(element, out double opacity)
            ? context with { GroupOpacity = opacity } : context;

    private void BeginOpacityPaint(Element element, Rect bounds, double opacity, int runStart)
    {
        bool animated = _animatedOpacity != null && _animatedOpacity.TryGetOpacity(element, out _);
        if (opacity >= 1 && !animated) return;
        var begin = new BeginOpacityRun(bounds, (float)opacity);
        _result.Runs.Insert(runStart, begin); _result.Runs.Add(new EndOpacityRun());
        _opacityOwners.Add(begin, element);
    }

    private static bool IsTransitionProperty(string property) => property is
        "transition-property" or "transition-duration" or "transition-delay" or "transition-timing-function";
}
