using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private static CssValue ComputedValue(CSSStyleDeclaration style, string property)
        => style.GetSpecifiedValue(property) ?? throw new InvalidOperationException("Missing computed property: " + property);
    private void PaintBox(Element element, CSSStyleDeclaration style, CSSStyleDeclaration computedValues,
        PositionedBoxResult box, PositionedPaintContext context,
        ConvexPolygon? polygon, in BoxDecorationParts decoration, ImageResource? image, int runStart)
    {
        var borders = decoration.Borders;
        bool uniformBorder = UniformBorder(borders);
        bool circular = UniformCircularCorners(decoration.Radii, out double radius);
        int contentStart = runStart;
        if (!PaintBackgroundDecorations(element, computedValues, box, in decoration, ref contentStart)
            && (decoration.Background.A > 0 || HasBorder(borders)))
        {
            PaintRun fill;
            if (polygon != null && !HasBorder(borders) && !HasCorners(decoration.Radii))
                fill = new PolygonRun(box.BorderBox, polygon, decoration.Background);
            else if (uniformBorder && circular)
                fill = new BoxRun(box.BorderBox, new BoxStyle(decoration.Background, borders.Top.Color, borders.Top.Width, radius));
            else
                fill = new BorderBoxRun(box.BorderBox, decoration.Background, borders, decoration.Radii);
            _result.Runs.Insert(contentStart++, fill);
        }
        if (image is { } resource && ImageLayout.Paint(style, resource, box.ContentBox) is { } imageRun) _result.Runs.Add(imageRun);
        if (context.ClipsX || context.ClipsY)
        {
            var clip = OverflowPaintRuns(context);
            _result.Runs.Insert(contentStart, clip.Begin);
            _result.Runs.Add(clip.End);
        }
        PaintScrollbars(element, style, box.PaddingBox);
        double outlineWidth = Length(style, "outline-width", _viewport.Width);
        if (style.GetPropertyValue("outline-style") != "none" && outlineWidth > 0)
        {
            if (!circular) Fail(element, "outline", "nonuniform corner outline geometry unavailable");
            double expansion = outlineWidth + Length(style, "outline-offset", _viewport.Width);
            Rect bounds = box.BorderBox;
            var outline = new Rect(bounds.X - expansion, bounds.Y - expansion, Math.Max(0, bounds.Width + 2 * expansion), Math.Max(0, bounds.Height + 2 * expansion));
            Rgba color = style.GetPropertyValue("outline-color") == "auto" ? Color(style, "color") : Color(style, "outline-color");
            _result.Runs.Add(new BoxRun(outline, new BoxStyle(default, color, outlineWidth, radius > 0 ? Math.Max(0, radius + expansion) : 0)));
        }
        if (polygon != null)
        { _result.Runs.Insert(runStart, new BeginPolygonClipRun(box.BorderBox, polygon)); _result.Runs.Add(new EndPolygonClipRun()); }
        BeginOpacityPaint(element, box.BorderBox, context.GroupOpacity, runStart);
    }
    private Rect EffectiveClipBounds(PositionedPaintContext context) => new(
        context.ClipsX ? context.ClipBounds.X : _viewport.X,
        context.ClipsY ? context.ClipBounds.Y : _viewport.Y,
        context.ClipsX ? context.ClipBounds.Width : _viewport.Width,
        context.ClipsY ? context.ClipBounds.Height : _viewport.Height);

    private static Rect Translate(Rect bounds, double dx, double dy) => bounds with { X = bounds.X + dx, Y = bounds.Y + dy };
    private static PositionedContainingBlock Translate(PositionedContainingBlock box, double dx, double dy) =>
        box with { ContentBox = Translate(box.ContentBox, dx, dy), PaddingBox = Translate(box.PaddingBox, dx, dy) };
    private void TranslateSubtree(int boxStart, int runStart, int contextStart, int deferredStart, double dx, double dy)
    {
        for (int i = boxStart; i < _result.Boxes.Count; i++)
        {
            var box = _result.Boxes[i];
            UsedLayoutGeometry? geometry = box.UsedGeometry;
            if (geometry is { } used) geometry = used with
            {
                Context = used.Context with { ClipBounds = Translate(used.Context.ClipBounds, dx, dy) },
                Transform = used.Transform with { Matrix = TranslateTransform(used.Transform.Matrix, dx, dy) }
            };
            _result.Boxes[i] = box with { Bounds = Translate(box.Bounds, dx, dy),
                ScrollOverflow = Translate(box.ScrollOverflow, dx, dy), UsedGeometry = geometry };
        }
        for (int i = runStart; i < _result.Runs.Count; i++)
            _result.Runs[i] = TranslatePaintRun(_result.Runs[i], dx, dy);
        for (int i = contextStart; i < _containing.Count; i++)
            if (_containing[i].Box is { } cb) _containing[i] = _containing[i] with { Box = Translate(cb, dx, dy) };
        for (int i = deferredStart; i < _deferred.Count; i++)
        {
            var item = _deferred[i];
            if (!item.Processed) _deferred[i] = item with { X = item.X + dx, Y = item.Y + dy, Flow = Translate(item.Flow, dx, dy) };
        }
    }
}
