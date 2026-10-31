using System.Collections.Generic;
using NecronomiDom.Dom;

namespace NecronomiDom.Paint;

public readonly record struct ScrollbarGeometry(Element Element, bool Horizontal, Rect Track, Rect Thumb);

public sealed partial class DisplayList
{
    public List<ScrollbarGeometry> Scrollbars { get; } = new();
}
