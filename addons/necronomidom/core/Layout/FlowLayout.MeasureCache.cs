using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    // Nested flex layout asks for the same intrinsic widths and natural heights while
    // resolving ancestors, final main sizes and paint. Only measurement is memoized:
    // Block still emits each box, paint context and hit target through its normal path.
    private readonly Dictionary<Element, PositionedIntrinsicWidth> _passIntrinsic = new();
    private readonly Dictionary<MeasureKey, double> _passMeasure = new();

    /// <summary>The plan inputs a natural content height depends on: available width, definite height
    /// and percentage basis, and the padding box edges. Origins, static positions, insets, margins and
    /// min/max constraints place or clamp the box afterwards, so the measure pass and the emitting pass
    /// of the same element share one entry even though their plans carry different positions.</summary>
    private readonly record struct MeasureKey(Element Element, double ContentWidth, double? DefiniteContentHeight,
        bool? DescendantHeightIsDefinite, PositionedEdges Edges);

    private PositionedIntrinsicWidth FlexIntrinsic(Element element, BoxStyleSnapshot style)
    {
        if (_passIntrinsic.TryGetValue(element, out var measured)) return measured;
        measured = MeasureFlexIntrinsic(element, style);
        measured = new(FlexLineSolver.Quantize(measured.Minimum), FlexLineSolver.Quantize(measured.Maximum));
        _passIntrinsic.Add(element, measured);
        return measured;
    }

    private double FlexMeasure(Element element, BoxStyleSnapshot style, PositionedBoxPlan plan)
    {
        var key = new MeasureKey(element, plan.ContentWidth, plan.DefiniteContentHeight, plan.DescendantHeightIsDefinite, plan.Edges);
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
