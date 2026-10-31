using System;
using System.Buffers;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private void CompletePaintPhase(PaintPhaseFrame frame, Element element, PositionedMode mode,
        Element? containingElement, PositionedPaintContext context, bool phaseBoundary)
    {
        if (_paintDepth > 1)
        {
            var parent = _paintFrames[_paintDepth - 2];
            if (phaseBoundary)
            {
                int runs = _result.Runs.Count - frame.RunStart, hits = _paintHitOrder.Count - frame.HitStart;
                var fragment = new PositionedPaintFragment
                {
                    Mode = mode, ContainingElement = containingElement,
                    Order = _paintTreeOrder[element], RunCount = runs, HitCount = hits,
                    Runs = runs > 0 ? ArrayPool<PaintRun>.Shared.Rent(runs) : Array.Empty<PaintRun>(),
                    Hits = hits > 0 ? ArrayPool<int>.Shared.Rent(hits) : Array.Empty<int>()
                };
                _result.Runs.CopyTo(frame.RunStart, fragment.Runs, 0, runs);
                _paintHitOrder.CopyTo(frame.HitStart, fragment.Hits, 0, hits);
                _result.Runs.RemoveRange(frame.RunStart, runs); _paintHitOrder.RemoveRange(frame.HitStart, hits);
                parent.Positioned.Add(fragment);
            }
            else
            {
                PaintRun? begin = null, end = null;
                if (context.ClipsX || context.ClipsY)
                {
                    (begin, end) = OverflowPaintRuns(context);
                }
                foreach (var original in frame.Positioned)
                {
                    var fragment = original;
                    // Absolute descendants escape static overflow outside their actual containing block.
                    // Opacity/clip-path boundaries remain atomic; their fragments were flushed before capture.
                    if (begin != null && (fragment.Mode != PositionedMode.Absolute || ContainsPaintBlock(element, fragment.ContainingElement)))
                        fragment.Overflow = new OverflowPaintLink(begin, end!, fragment.X, fragment.Y, fragment.Overflow);
                    parent.Positioned.Add(fragment);
                }
                frame.Positioned.Clear();
            }
        }
        _paintDepth--;
    }
    private static bool ContainsPaintBlock(Element owner, Element? containingElement)
    {
        if (owner.ParentElement == null) return true;
        for (var ancestor = containingElement; ancestor != null; ancestor = ancestor.ParentElement)
            if (ReferenceEquals(ancestor, owner)) return true;
        return false;
    }
    private static void TranslateHeldPaint(PaintPhaseFrame frame, double dx, double dy)
    {
        for (int i = 0; i < frame.Positioned.Count; i++)
        { var fragment = frame.Positioned[i]; fragment.X += dx; fragment.Y += dy; frame.Positioned[i] = fragment; }
    }
    private PaintRun TranslatePaintRun(PaintRun run, double dx, double dy)
    {
        if (dx == 0 && dy == 0) return run;
        PaintRun translated = run switch
        {
            BoxRun box => box with { Bounds = Translate(box.Bounds, dx, dy) },
            BorderBoxRun border => border with { Bounds = Translate(border.Bounds, dx, dy) },
            TextRun text => text with { X = text.X + dx, Baseline = text.Baseline + dy,
                ControlLine = text.TextControl == null ? text.ControlLine : Translate(text.ControlLine, dx, dy),
                InlineFragment = text.InlineFragment is { } fragment ? fragment with { Bounds = Translate(fragment.Bounds, dx, dy) } : null,
                TextControlViewport = text.TextControl == null ? text.TextControlViewport : Translate(text.TextControlViewport, dx, dy) },
            TextEffectsRun effects => effects with
                { Text = (TextRun)TranslatePaintRun(effects.Text, dx, dy) },
            ImageRun image => image with { Destination = Translate(image.Destination, dx, dy) },
            BackgroundImageRun background => background with
            {
                Image = background.Image with { Destination = Translate(background.Image.Destination, dx, dy) },
                Clip = new EllipticalClipGeometry(Translate(background.Clip.Bounds, dx, dy), background.Clip.Radii)
            },
            GradientRun gradient => gradient with
            {
                Tile = Translate(gradient.Tile, dx, dy),
                Clip = new EllipticalClipGeometry(Translate(gradient.Clip.Bounds, dx, dy), gradient.Clip.Radii)
            },
            PolygonRun polygon => new PolygonRun(Translate(polygon.Bounds, dx, dy), polygon.Geometry, polygon.Color),
            BeginRectClipRun clip => new BeginRectClipRun(Translate(clip.Bounds, dx, dy)),
            BeginRoundedRectClipRun clip => new BeginRoundedRectClipRun(Translate(clip.Bounds, dx, dy), clip.Radius),
            BeginEllipticalClipRun clip => new BeginEllipticalClipRun(Translate(clip.Bounds, dx, dy), clip.Radii),
            EndEllipticalClipRun clip => new EndEllipticalClipRun(Translate(clip.Bounds, dx, dy)),
            BeginPolygonClipRun clip => new BeginPolygonClipRun(Translate(clip.Bounds, dx, dy), clip.Geometry),
            BeginOpacityRun opacity => new BeginOpacityRun(Translate(opacity.Bounds, dx, dy), opacity.Opacity),
            BeginTransformRun transform => new BeginTransformRun(Translate(transform.Bounds, dx, dy), TranslateTransform(transform.Transform, dx, dy)),
            EndTransformRun transform => new EndTransformRun(Translate(transform.Bounds, dx, dy)),
            EndRectClipRun or EndRoundedRectClipRun or EndPolygonClipRun or EndOpacityRun => run,
            _ => throw new NotSupportedException("Cannot translate unknown positioned subtree run: " + run.GetType().Name)
        };
        if (_opacityOwners.Remove(run, out var owner)) _opacityOwners.Add(translated, owner);
        if (_transformOwners.Remove(run, out var transformOwner)) _transformOwners.Add(translated, transformOwner);
        return translated;
    }
}
