using System;
using NecronomiDom.Paint;

namespace NecronomiDom.Dom;

public sealed partial class DocumentInput
{
    private void IndexBounds(DisplayList display)
    {
        foreach (var box in display.Boxes)
        {
            if (box.Element is not HTMLElement element) continue;
            var bounds = box.VisualBounds;
            if (_bounds.TryGetValue(element, out var previous))
            {
                double x = Math.Min(previous.X, bounds.X), y = Math.Min(previous.Y, bounds.Y);
                bounds = new Rect(x, y, Math.Max(previous.X + previous.Width, bounds.X + bounds.Width) - x,
                    Math.Max(previous.Y + previous.Height, bounds.Y + bounds.Height) - y);
            }
            _bounds[element] = bounds;
        }
    }
    private int CompareTabOrder(HTMLElement a, HTMLElement b)
    {
        int ai = a.TabIndex > 0 ? a.TabIndex : int.MaxValue, bi = b.TabIndex > 0 ? b.TabIndex : int.MaxValue;
        int order = ai.CompareTo(bi); return order != 0 ? order : _treeOrder[a].CompareTo(_treeOrder[b]);
    }
    private void RecordTreeOrder(Node node, ref int order)
    {
        if (node is HTMLElement element) _treeOrder[element] = order++;
        for (var child = node.FirstChild; child != null; child = child.NextSibling) RecordTreeOrder(child, ref order);
    }
}
