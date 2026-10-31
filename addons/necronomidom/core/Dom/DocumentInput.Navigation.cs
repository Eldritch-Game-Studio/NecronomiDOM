using System;
using NecronomiDom.Paint;

namespace NecronomiDom.Dom;

public sealed partial class DocumentInput
{
    /// <summary>CSS Spatial Navigation 1 on visible flow boxes; no engine-native focus search.</summary>
    public bool Navigate(string direction)
    {
        if (direction is not ("up" or "down" or "left" or "right")) throw new ArgumentException("Unknown navigation direction.", nameof(direction));
        ValidateTransientState(); _document.SetFocusVisible(true);
        var focused = _document.FocusedElement;
        if (focused == null) return MoveSequential(false);
        // Buttons and links have no horizontal value/caret action. A canceled Arrow key still vetoes navigation.
        if (direction is "left" or "right")
        {
            string arrow = direction == "left" ? "ArrowLeft" : "ArrowRight";
            _navigationKey = true;
            try
            {
                if (!Key(new KeyboardEvent("keydown", new KeyboardEventInit { Key = arrow, Code = arrow, Bubbles = true, Cancelable = true, Composed = true }))) return false;
                Key(new KeyboardEvent("keyup", new KeyboardEventInit { Key = arrow, Code = arrow, Bubbles = true, Cancelable = true, Composed = true }));
            }
            finally { _navigationKey = false; }
        }
        if (!_bounds.TryGetValue(focused, out var origin)) return false;
        Element? container = ContainerOf(focused);
        Element? exitedContainer = null;
        while (container != null)
        {
            var style = _styles.GetComputedStyle(container);
            if (style.GetPropertyValue("spatial-navigation-action") == "scroll") return ScrollDirection(focused, direction);
            bool grid = style.GetPropertyValue("spatial-navigation-function") == "grid";
            HTMLElement? best = null; double bestScore = double.PositiveInfinity, bestTie = double.PositiveInfinity;
            foreach (var box in _display.Boxes)
            {
                if (box.Element is not HTMLElement candidate || ReferenceEquals(candidate, focused) || !_document.IsFocusable(candidate) || candidate.TabIndex < 0 || !container.Contains(candidate) || exitedContainer?.Contains(candidate) == true) continue;
                var b = box.VisualBounds;
                if (!SharesScrollContainer(focused, candidate) && (b.X + b.Width <= 0 || b.Y + b.Height <= 0 || b.X >= _width || b.Y >= _height)) continue;
                if (!InDirection(origin, b, direction)) continue;
                double primary = PrimaryGap(origin, b, direction);
                bool horizontal = direction is "left" or "right";
                double overlap = horizontal ? Overlap(origin.Y, origin.Height, b.Y, b.Height) : Overlap(origin.X, origin.Width, b.X, b.Width);
                double orthogonal = horizontal ? Gap(origin.Y, origin.Height, b.Y, b.Height) : Gap(origin.X, origin.Width, b.X, b.Width);
                double extent = horizontal ? origin.Height : origin.Width;
                double score = grid ? (overlap > 0 ? 0 : 1e15) + primary : Math.Sqrt(primary * primary + orthogonal * orthogonal) + (orthogonal + extent / 2) * (horizontal ? 30 : 2) - overlap / extent * 5;
                double tie = grid ? (overlap > 0 ? overlap : orthogonal) : 0;
                if (score < bestScore || score == bestScore && tie < bestTie)
                { best = candidate; bestScore = score; bestTie = tie; }
            }
            if (best != null)
            {
                if (!focused.DispatchUserEvent(new NavigationEvent("navbeforefocus", direction, best))) return false;
                return _document.Focus(best, true);
            }
            if (ScrollDirection(focused, direction)) return true;
            if (!container.DispatchUserEvent(new NavigationEvent("navnotarget", direction))) return false;
            if (ReferenceEquals(container, _document.DocumentElement)) return false;
            if (_bounds.TryGetValue(container as HTMLElement ?? focused, out var containerBounds)) origin = containerBounds;
            exitedContainer = container;
            container = ContainerOf(container);
        }
        return false;
    }
    private Element? ContainerOf(Element element)
    {
        for (var parent = element.ParentElement; parent != null; parent = parent.ParentElement)
            if (_styles.GetComputedStyle(parent).GetPropertyValue("spatial-navigation-contain") == "contain" || ReferenceEquals(parent, _document.DocumentElement)) return parent;
        return _document.DocumentElement;
    }
    private static bool InDirection(Rect a, Rect b, string dir) => dir switch
    { "down" => b.Y >= a.Y + a.Height, "up" => b.Y + b.Height <= a.Y, "right" => b.X >= a.X + a.Width, _ => b.X + b.Width <= a.X };
    private static double PrimaryGap(Rect a, Rect b, string dir) => dir switch
    { "down" => b.Y - a.Y - a.Height, "up" => a.Y - b.Y - b.Height, "right" => b.X - a.X - a.Width, _ => a.X - b.X - b.Width };
    private static double Overlap(double a, double al, double b, double bl) => Math.Max(0, Math.Min(a + al, b + bl) - Math.Max(a, b));
    private static double Gap(double a, double al, double b, double bl) => Math.Max(0, Math.Max(a, b) - Math.Min(a + al, b + bl));
}
