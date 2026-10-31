using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    // Nested flex layout asks for the same intrinsic widths and natural heights while
    // resolving ancestors, final main sizes and paint. Only measurement is memoized:
    // Block still emits each box, paint context and hit target through its normal path.
    // The full plan distinguishes wrapping widths and definite percentage-height bases.
    private readonly Dictionary<Element, PositionedIntrinsicWidth> _passIntrinsic = new();
    private readonly Dictionary<(Element Element, PositionedBoxPlan Plan), double> _passMeasure = new();

    private PositionedIntrinsicWidth FlexIntrinsic(Element element, CSSStyleDeclaration style)
    {
        if (_passIntrinsic.TryGetValue(element, out var measured)) return measured;
        measured = MeasureFlexIntrinsic(element, style);
        measured = new(FlexLineSolver.Quantize(measured.Minimum), FlexLineSolver.Quantize(measured.Maximum));
        _passIntrinsic.Add(element, measured);
        return measured;
    }

    private double FlexMeasure(Element element, CSSStyleDeclaration style, PositionedBoxPlan plan)
    {
        var key = (element, plan);
        if (_passMeasure.TryGetValue(key, out double measured)) return measured;
        measured = MeasureFlexHeight(element, style, plan);
        _passMeasure.Add(key, measured);
        return measured;
    }

    private void ClearMeasurements()
    {
        // No data survives a Layout call, including a failed one. Text, styles, image
        // resources, font metrics and animation samples are read afresh next frame.
        _passIntrinsic.Clear();
        _passMeasure.Clear();
    }
}
