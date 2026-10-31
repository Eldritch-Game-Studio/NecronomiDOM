namespace NecronomiDom.Paint;

public sealed partial record LayoutBox
{
    /// <summary>Untransformed overflow propagated to the parent; clipped axes end at the padding edge.</summary>
    public Rect ScrollOverflow { get; init; }
}
