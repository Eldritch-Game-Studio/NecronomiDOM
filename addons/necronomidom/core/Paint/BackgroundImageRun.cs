using NecronomiDom.Dom;
using NecronomiDom.Layout;

namespace NecronomiDom.Paint;

/// <summary>A used background image tile and its actual CSS background clipping edge.</summary>
public sealed record BackgroundImageRun(ImageRun Image, EllipticalClipGeometry Clip) : PaintRun;

/// <summary>Loads an authored CSS image URL without manufacturing a replaced DOM element.</summary>
public interface IBackgroundImageSource
{
    ImageResource LoadBackground(Element owner, string sourceUrl);
}
