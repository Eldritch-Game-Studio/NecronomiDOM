using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public enum PositionedMode { Static, Relative, Absolute }
public readonly record struct PositionedContainingBlock(Rect ContentBox, Rect PaddingBox, bool HeightIsDefinite);
public readonly record struct PositionedIntrinsicWidth(double Minimum, double Maximum);
public readonly record struct PositionedEdges(double PaddingTop, double PaddingRight, double PaddingBottom, double PaddingLeft,
    double BorderTop, double BorderRight, double BorderBottom, double BorderLeft)
{
    public double Horizontal => PaddingLeft + PaddingRight + BorderLeft + BorderRight;
    public double Vertical => PaddingTop + PaddingBottom + BorderTop + BorderBottom;
}
public readonly record struct PositionedPaintContext(bool ClipsX, bool ClipsY, Rect ClipBounds, double GroupOpacity, double ClipRadius)
{
    /// <summary>Actual final padding-edge ellipses; ClipRadius is only the uniform circular fast path.</summary>
    public BorderRadii ClipRadii { get; init; }
}

/// <summary>Prepared used sizes. An absent definite height means CSS auto/indefinite, never missing layout work.</summary>
public readonly record struct PositionedBoxPlan
{
    public PositionedMode Mode { get; init; }
    public double ContentWidth { get; init; }
    public double? DefiniteContentHeight { get; init; }
    /// <summary>Flex may know a final used height without establishing a definite percentage-height basis.</summary>
    public bool? DescendantHeightIsDefinite { get; init; }
    public double MinContentHeight { get; init; }
    public double MaxContentHeight { get; init; }
    public PositionedEdges Edges { get; init; }
    public double PreLayoutBorderBoxOriginX { get; init; }
    public double PreLayoutBorderBoxOriginY { get; init; }
    public double ContentOriginX => PreLayoutBorderBoxOriginX + Edges.BorderLeft + Edges.PaddingLeft;
    public double ContentOriginY => PreLayoutBorderBoxOriginY + Edges.BorderTop + Edges.PaddingTop;
    public bool ExcludedFromFlow => Mode == PositionedMode.Absolute;
    internal Rect Basis { get; init; }
    internal double StaticX { get; init; }
    internal double StaticY { get; init; }
    internal double? Top { get; init; }
    internal double? Bottom { get; init; }
    public double? MarginTop { get; init; }
    public double? MarginBottom { get; init; }
    internal double FlowMarginTop { get; init; }
    internal double FlowMarginBottom { get; init; }
}
public readonly record struct PositionedBoxResult(Rect BorderBox, Rect ContentBox, Rect PaddingBox,
    double FlowContribution, bool EstablishesContainingBlock, PositionedContainingBlock DescendantContainingBlock);
