using System;

namespace NecronomiDom.Dom;

/// <summary>Horizontal LTR CSSOM scroll geometry, owned by layout, with fractional offsets.</summary>
internal sealed class ElementScrollState
{
    internal double Left, Top, Width, Height, ClientWidth, ClientHeight, LayoutLeft, LayoutTop, GutterRight, GutterBottom;
    internal bool X, Y, UserX, UserY, HoverX, HoverY;
    internal double MaxLeft => X ? Math.Max(0, Width - ClientWidth) : 0;
    internal double MaxTop => Y ? Math.Max(0, Height - ClientHeight) : 0;
    internal bool Set(double left, double top)
    {
        left = Math.Clamp(double.IsFinite(left) ? left : 0, 0, MaxLeft);
        top = Math.Clamp(double.IsFinite(top) ? top : 0, 0, MaxTop);
        if (Left == left && Top == top) return false;
        Left = left; Top = top; return true;
    }
}

public partial class Element
{
    internal ElementScrollState? ScrollState;
    public double ScrollLeft { get => ScrollState?.Left ?? 0; set => ScrollTo(value, ScrollTop); }
    public double ScrollTop { get => ScrollState?.Top ?? 0; set => ScrollTo(ScrollLeft, value); }
    public int ScrollWidth => (int)Math.Round(ScrollState?.Width ?? 0, MidpointRounding.AwayFromZero);
    public int ScrollHeight => (int)Math.Round(ScrollState?.Height ?? 0, MidpointRounding.AwayFromZero);
    public int ClientWidth => (int)Math.Round(ScrollState?.ClientWidth ?? 0, MidpointRounding.AwayFromZero);
    public int ClientHeight => (int)Math.Round(ScrollState?.ClientHeight ?? 0, MidpointRounding.AwayFromZero);
    public void ScrollTo(double left, double top) => NodeDocument.PointerInput?.Scroll(this, left, top);
    public void ScrollBy(double left, double top) => ScrollTo(ScrollLeft + left, ScrollTop + top);
    public void ScrollIntoView(ScrollIntoViewOptions? options = null) => NodeDocument.PointerInput?.ScrollIntoView(this, options);
}

public sealed class ScrollIntoViewOptions
{
    public string Block { get; init; } = "start";
    public string Inline { get; init; } = "nearest";
}
