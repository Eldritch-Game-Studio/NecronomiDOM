using System;
using NecronomiDom.Paint;

namespace NecronomiDom.Dom;

public sealed partial class DocumentInput
{
    private ScrollbarGeometry? _scrollDrag;
    private double _scrollDragCoordinate, _scrollDragOffset;
    private bool TryScrollbarPoint(ScrollbarGeometry bar, double x, double y, bool requireHit, out double localX, out double localY)
    {
        foreach (var box in _display.Boxes)
        {
            if (!ReferenceEquals(box.Element, bar.Element) || box.UsedGeometry is not { } used) continue;
            if ((!requireHit || box.PointerEvents && box.ContainsPoint(x, y)) && box.TryMapPoint(x, y, out localX, out localY))
            {
                localX -= used.Context.ClipBounds.X; localY -= used.Context.ClipBounds.Y;
                return !requireHit || bar.Track.Contains(localX, localY);
            }
        }
        localX = localY = 0; return false;
    }
    private bool HitScrollbar(double x, double y, out ScrollbarGeometry bar, out double localX, out double localY)
    {
        for (int i = _display.Boxes.Count - 1; i >= 0; i--)
            foreach (var candidate in _display.Scrollbars)
                if (ReferenceEquals(candidate.Element, _display.Boxes[i].Element) && !Document.IsInert(candidate.Element)
                    && TryScrollbarPoint(candidate, x, y, true, out localX, out localY))
                { bar = candidate; return true; }
        bar = default; localX = localY = 0; return false;
    }
    private bool ScrollbarButton(double x, double y, bool down)
    {
        if (_scrollDrag != null && !down) { _scrollDrag = null; return true; }
        if (!down || !HitScrollbar(x, y, out var bar, out double localX, out double localY) || bar.Element.ScrollState is not { } state) return false;
        if (bar.Thumb.Contains(localX, localY))
        {
            _scrollDrag = bar;
            _scrollDragCoordinate = bar.Horizontal ? localX : localY;
            _scrollDragOffset = bar.Horizontal ? state.Left : state.Top;
        }
        else
        {
            double sign = bar.Horizontal ? (localX < bar.Thumb.X ? -1 : 1) : (localY < bar.Thumb.Y ? -1 : 1);
            Scroll(bar.Element, state.Left + (bar.Horizontal ? sign * state.ClientWidth * 0.875 : 0),
                state.Top + (bar.Horizontal ? 0 : sign * state.ClientHeight * 0.875));
            // Retain the native stream through up, without activating content under the track.
            _scrollDrag = bar; _scrollDragCoordinate = bar.Horizontal ? localX : localY;
            _scrollDragOffset = bar.Horizontal ? state.Left : state.Top;
        }
        return true;
    }
    private bool MoveScrollbar(double x, double y)
    {
        bool repaint = false;
        foreach (var bar in _display.Scrollbars)
        {
            if (bar.Element.ScrollState is not { } state) continue;
            bool hover = TryScrollbarPoint(bar, x, y, true, out double hx, out double hy) && bar.Thumb.Contains(hx, hy);
            if (bar.Horizontal) { repaint |= state.HoverX != hover; state.HoverX = hover; }
            else { repaint |= state.HoverY != hover; state.HoverY = hover; }
        }
        if (repaint) ScrollChanged?.Invoke();
        if (_scrollDrag is not { } drag) return false;
        if (!drag.Element.IsConnected || Document.IsInert(drag.Element) || drag.Element.ScrollState is not { } s
            || !TryScrollbarPoint(drag, x, y, false, out double localX, out double localY))
        { _scrollDrag = null; return true; }
        double track = drag.Horizontal ? drag.Track.Width : drag.Track.Height;
        double content = drag.Horizontal ? s.Width : s.Height, client = drag.Horizontal ? s.ClientWidth : s.ClientHeight;
        double thumb = Math.Min(track, Math.Max(16, content <= 0 ? track : track * client / content));
        double delta = (drag.Horizontal ? localX : localY) - _scrollDragCoordinate;
        double offset = track <= thumb ? 0 : _scrollDragOffset + delta * (content - client) / (track - thumb);
        Scroll(drag.Element, drag.Horizontal ? offset : s.Left, drag.Horizontal ? s.Top : offset);
        return true;
    }
    private void ClearScrollbarHover()
    {
        _scrollDrag = null;
        bool repaint = false;
        foreach (var bar in _display.Scrollbars)
            if (bar.Element.ScrollState is { } state)
            { repaint |= state.HoverX || state.HoverY; state.HoverX = state.HoverY = false; }
        if (repaint) ScrollChanged?.Invoke();
    }
}
