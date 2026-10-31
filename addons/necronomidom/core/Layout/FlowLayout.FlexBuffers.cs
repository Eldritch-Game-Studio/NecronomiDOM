using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private sealed class FlexCollectionBuffers
    {
        internal readonly List<FlexChild> Children = new();
        internal readonly List<Element> Absolute = new();
        internal readonly StringBuilder Text = new();
        internal void Clear() { Children.Clear(); Absolute.Clear(); Text.Clear(); }
    }
    private readonly Stack<FlexCollectionBuffers> _flexCollectionPool = new();
    internal int FlexCollectionBufferCount { get; private set; }

    private double FlexChildren(Element element, CSSStyleDeclaration style, PositionedBoxPlan plan,
        int containing, bool emit, bool measureScrollOverflow = false)
    {
        if (!_flexCollectionPool.TryPop(out var buffers))
        {
            buffers = new FlexCollectionBuffers();
            FlexCollectionBufferCount++;
        }
        // A recursive measurement/paint owns a different collection set until this call returns.
        // Clear references before returning even when a caller-supplied shaper/image source throws.
        try { return FlexChildrenCore(element, style, plan, containing, emit, measureScrollOverflow, buffers); }
        finally { buffers.Clear(); _flexCollectionPool.Push(buffers); }
    }
}
