using System;
using System.Collections.Generic;
using NecronomiDom.Paint;

namespace NecronomiDom.Dom;

public sealed partial class DocumentInput
{
    public Action? ScrollChanged { get; set; }
    private readonly HashSet<Element> _pendingScrollEvents = new();
    private readonly List<Element> _scrollDispatch = new();
    private bool _navigationKey;
    internal bool Scroll(Element element, double left, double top)
    {
        if (_disposed || !element.IsConnected || !TryScrollViewport(element, out _) || element.ScrollState is not { } state || !state.Set(left, top)) return false;
        _pendingScrollEvents.Add(element); ScrollChanged?.Invoke(); return true;
    }
    public void DispatchScrollEvents()
    {
        _scrollDispatch.Clear();
        foreach (var element in _pendingScrollEvents) _scrollDispatch.Add(element);
        _pendingScrollEvents.Clear();
        for (int i = 0; i < _scrollDispatch.Count && !_disposed; i++)
            if (_scrollDispatch[i].IsConnected) _scrollDispatch[i].DispatchUserEvent(new Event("scroll"));
        _scrollDispatch.Clear();
    }
    private bool TryScrollViewport(Element element, out Rect viewport)
    {
        foreach (var box in _display.Boxes)
            if (ReferenceEquals(box.Element, element) && box.UsedGeometry is { } geometry)
            { viewport = geometry.Context.ClipBounds; return true; }
        viewport = default; return false;
    }
    /// <summary>Pixel-mode wheel defaults; a canceled trusted wheel never changes offsets.</summary>
    public bool Wheel(double x, double y, double deltaX, double deltaY)
    {
        if (_disposed || HitTarget(x, y) is not { } target) return false;
        int version = _inputVersion;
        if (!Dispatch(target, new WheelEvent("wheel", new WheelEventInit { Bubbles = true, Cancelable = true,
            Composed = true, ClientX = x, ClientY = y, DeltaX = deltaX, DeltaY = deltaY }))) return false;
        if (_disposed || version != _inputVersion) return false;
        return ScrollChain(target, deltaX, deltaY);
    }
    private bool ScrollChain(Element? target, double dx, double dy)
    {
        bool moved = false;
        for (var current = target; current != null && (dx != 0 || dy != 0); current = current.ParentElement)
        {
            if (Document.IsInert(current) || current.ScrollState is not { } state) continue;
            double left = state.Left, top = state.Top;
            if (Scroll(current, left + (state.UserX ? dx : 0), top + (state.UserY ? dy : 0)))
            {
                moved = true;
                // An axis chains outward only when its inner scroller is already at the edge.
                if (left != state.Left) dx = 0;
                if (top != state.Top) dy = 0;
            }
        }
        return moved;
    }
    public bool HasUserScrollAncestor(Element? element)
    {
        for (var current = element; current != null; current = current.ParentElement)
            if (current.ScrollState is { } s && (s.UserX && s.MaxLeft > 0 || s.UserY && s.MaxTop > 0)) return true;
        return false;
    }
    private static bool SharesScrollContainer(Element a, Element b)
    {
        for (var current = a.ParentElement; current != null; current = current.ParentElement)
            if (current.ScrollState is { } s && (s.UserX || s.UserY) && current.Contains(b)) return true;
        return false;
    }
    private bool ScrollDirection(Element? element, string direction) => ScrollChain(element,
        direction == "left" ? -40 : direction == "right" ? 40 : 0,
        direction == "up" ? -40 : direction == "down" ? 40 : 0);
    private bool ScrollKey(HTMLElement? focused, KeyboardEvent input)
    {
        if (_navigationKey || input.AltKey || input.CtrlKey || input.MetaKey) return false;
        var target = focused ?? _document.Body;
        if (input.Key is "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight")
            return ScrollDirection(target, input.Key[5..].ToLowerInvariant());
        if (input.Key == " " && ActivationTarget(focused) != null) return false;
        for (var current = (Element?)target; current != null; current = current.ParentElement)
        {
            if (current.ScrollState is not { UserY: true } state || state.MaxTop <= 0) continue;
            double next = input.Key switch
            {
                "Home" => 0, "End" => state.MaxTop,
                "PageUp" => state.Top - state.ClientHeight * 0.875,
                "PageDown" => state.Top + state.ClientHeight * 0.875,
                " " => state.Top + state.ClientHeight * 0.875 * (input.ShiftKey ? -1 : 1), _ => state.Top
            };
            if (Scroll(current, state.Left, next)) return true;
        }
        return false;
    }
    internal void ScrollIntoView(Element element, ScrollIntoViewOptions? options = null, bool focus = false)
    {
        string block = options?.Block ?? (focus ? "center" : "start"), inline = options?.Inline ?? (focus ? "center" : "nearest");
        ValidateAlignment(block); ValidateAlignment(inline);
        Rect target = default; bool found = false;
        foreach (var box in _display.Boxes)
            if (ReferenceEquals(box.Element, element)) { target = CurrentScrollRect(element, box.VisualBounds); found = true; break; }
        if (!found) return;
        for (var current = element.ParentElement; current != null; current = current.ParentElement)
        {
            if (current.ScrollState is not { } state || !TryScrollViewport(current, out var viewport)) continue;
            viewport = CurrentScrollRect(current, viewport);
            double dx = Align(target.X, target.Width, viewport.X, viewport.Width, inline, focus);
            double dy = Align(target.Y, target.Height, viewport.Y, viewport.Height, block, focus);
            double left = state.Left, top = state.Top;
            Scroll(current, left + dx, top + dy);
            target = target with { X = target.X - (state.Left - left), Y = target.Y - (state.Top - top) };
        }
    }
    private static Rect CurrentScrollRect(Element element, Rect bounds)
    {
        for (var parent = element.ParentElement; parent != null; parent = parent.ParentElement)
            if (parent.ScrollState is { } s)
                bounds = bounds with { X = bounds.X - s.Left + s.LayoutLeft, Y = bounds.Y - s.Top + s.LayoutTop };
        return bounds;
    }
    private static void ValidateAlignment(string value)
    { if (value is not ("start" or "end" or "center" or "nearest")) throw new ArgumentException("Unknown scroll alignment: " + value); }
    private static double Align(double start, double size, double port, double extent, string alignment, bool ifNeeded)
    {
        double before = start - port, after = start + size - port - extent;
        if (ifNeeded && before >= 0 && after <= 0) return 0;
        // HTML focus requests center on both axes; pinned Chrome uses nearest for a
        // partially visible target, retaining the visible portion rather than recentering.
        if (ifNeeded && start < port + extent && start + size > port) alignment = "nearest";
        return alignment switch
        {
            "start" => before, "end" => after, "center" => before + (size - extent) / 2,
            _ => before < 0 && after > 0 ? 0 : before < 0 ? (size <= extent ? before : after)
                : after > 0 ? (size <= extent ? after : before) : 0
        };
    }
}
