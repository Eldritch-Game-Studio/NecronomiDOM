using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private static readonly CssListValue AutoBackgroundAxes = new(
        new CssValue[] { new CssKeywordValue("auto"), new CssKeywordValue("auto") }, CssListSeparator.Space);
    // Called before the URL-only producer. All inputs are computed typed values, not CSSOM strings.
    private bool PaintLayeredBackgroundDecorations(Element element, CSSStyleDeclaration values, CssValue images,
        PositionedBoxResult box, in BoxDecorationParts decoration, ref int contentStart)
    {
        if (element.LocalName is "html" or "body")
            throw new NotSupportedException("Root/body canvas background image propagation is not yet supported.");
        int count = images is CssListValue { Separator: CssListSeparator.Comma } list ? list.Items.Count : 1;
        var colorClip = BackgroundEdge(LayerKeyword(values, "background-clip", count - 1), box, in decoration);
        if (decoration.Background.A > 0 && !colorClip.IsEmpty)
        {
            PaintRun color = UniformCircularCorners(colorClip.Radii, out double radius)
                ? new BoxRun(colorClip.Bounds, new BoxStyle(decoration.Background, default, 0, radius))
                : new BorderBoxRun(colorClip.Bounds, decoration.Background, default, colorClip.Radii);
            _result.Runs.Insert(contentStart++, color);
        }
        // CSS lists the frontmost image first; display runs paint back to front.
        for (int layer = count - 1; layer >= 0; layer--)
        {
            var image = LayerValue(images, layer);
            if (image is CssKeywordValue { Value: "none" }) continue;
            var clip = BackgroundEdge(LayerKeyword(values, "background-clip", layer), box, in decoration);
            if (clip.IsEmpty) continue;
            Rect origin = BackgroundEdge(LayerKeyword(values, "background-origin", layer), box, in decoration).Bounds;
            if (origin.Width <= 0 || origin.Height <= 0) continue;
            var size = LayerValue(BackgroundValue(values, "background-size"), layer);
            var px = LayerValue(BackgroundValue(values, "background-position-x"), layer);
            var py = LayerValue(BackgroundValue(values, "background-position-y"), layer);
            var repeat = LayerValue(BackgroundValue(values, "background-repeat"), layer);
            if (image is CssGradientValue gradient)
            {
                var dimensions = GeneratedSize(size, origin);
                if (dimensions.Width <= 0 || dimensions.Height <= 0) continue;
                RepeatAxes(repeat, out string rx, out string ry);
                var horizontal = BackgroundAxis(origin.X, origin.Width, dimensions.Width, px, rx);
                var vertical = BackgroundAxis(origin.Y, origin.Height, dimensions.Height, py, ry);
                var tile = new Rect(horizontal.Position, vertical.Position, horizontal.Size, vertical.Size);
                if (!horizontal.Repeat && (tile.X + tile.Width <= clip.Bounds.X || tile.X >= clip.Bounds.X + clip.Bounds.Width)
                    || !vertical.Repeat && (tile.Y + tile.Height <= clip.Bounds.Y || tile.Y >= clip.Bounds.Y + clip.Bounds.Height)) continue;
                var used = GradientLayout.Resolve(gradient, tile.Width, tile.Height);
                _result.Runs.Insert(contentStart++, new GradientRun(used, tile, clip,
                    horizontal.Step, vertical.Step, horizontal.Repeat, vertical.Repeat));
            }
            else if (image is CssUrlValue url)
            {
                // Keep the existing URL sampling/resource/crop contract. Repeating URL images remain unavailable.
                if (!NoRepeat(repeat)) throw new NotSupportedException("Runtime URL background images require no-repeat on both axes.");
                ImageSampling sampling = LayerKeyword(values, "image-rendering", 0) switch
                {
                    "auto" or "smooth" or "high-quality" => ImageSampling.Linear,
                    "crisp-edges" => ImageSampling.Nearest, "pixelated" => ImageSampling.Pixelated,
                    var unsupported => throw new NotSupportedException("Unsupported background image sampling: " + unsupported)
                };
                // The initial computed value is a single auto; the existing URL helper accepts explicit axes.
                if (size is CssKeywordValue { Value: "auto" }) size = AutoBackgroundAxes;
                PaintUrlBackgroundTile(element, url, origin, clip, size, px, py, sampling, ref contentStart);
            }
            else throw new NotSupportedException("Unsupported computed generated background image type: " + image.GetType().Name);
        }
        if (HasBorder(decoration.Borders))
        {
            var sides = decoration.Borders;
            PaintRun border = UniformBorder(sides) && UniformCircularCorners(decoration.Radii, out double radius)
                ? new BoxRun(box.BorderBox, new BoxStyle(default, sides.Top.Color, sides.Top.Width, radius))
                : new BorderBoxRun(box.BorderBox, default, sides, decoration.Radii);
            _result.Runs.Insert(contentStart++, border);
        }
        return true;
    }

    private static CssValue LayerValue(CssValue value, int index) => value is CssListValue { Separator: CssListSeparator.Comma } list
        ? list.Items[index % list.Items.Count] : value;
    private static string LayerKeyword(CSSStyleDeclaration values, string property, int layer) =>
        LayerValue(BackgroundValue(values, property), layer) is CssKeywordValue keyword ? keyword.Value
        : throw new NotSupportedException("Unresolved background layer keyword: " + property);
    private static ImageSize GeneratedSize(CssValue value, Rect origin)
    {
        if (value is CssKeywordValue { Value: "cover" or "contain" or "auto" }) return new(origin.Width, origin.Height);
        if (value is not CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } axes)
            throw new NotSupportedException("Generated background-size requires computed two-axis dimensions.");
        return new(axes.Items[0] is CssKeywordValue { Value: "auto" } ? origin.Width : Math.Max(0, ImageLayout.Length(axes.Items[0], origin.Width)),
            axes.Items[1] is CssKeywordValue { Value: "auto" } ? origin.Height : Math.Max(0, ImageLayout.Length(axes.Items[1], origin.Height)));
    }
    private static void RepeatAxes(CssValue value, out string x, out string y)
    {
        if (value is CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } axes
            && axes.Items[0] is CssKeywordValue a && axes.Items[1] is CssKeywordValue b) { x = a.Value; y = b.Value; return; }
        if (value is not CssKeywordValue keyword) throw new NotSupportedException("Unresolved background-repeat.");
        x = keyword.Value == "repeat-y" ? "no-repeat" : keyword.Value == "repeat-x" ? "repeat" : keyword.Value;
        y = keyword.Value == "repeat-x" ? "no-repeat" : keyword.Value == "repeat-y" ? "repeat" : keyword.Value;
    }
    private static (double Position, double Size, double Step, bool Repeat) BackgroundAxis(double start, double extent,
        double size, CssValue position, string repeat)
    {
        if (repeat == "round") size = extent / Math.Max(1, Math.Floor(extent / size + .5));
        double phase = start + ImageLayout.Length(position, extent - size), step = size;
        bool repeating = repeat is "repeat" or "round";
        if (repeat == "space")
        {
            double count = Math.Floor(extent / size);
            if (count >= 2) { phase = start; step = (extent - size) / (count - 1); repeating = true; }
        }
        else if (repeat is not ("no-repeat" or "repeat" or "round"))
            throw new NotSupportedException("Unsupported generated background repeat: " + repeat);
        return (phase, size, step, repeating);
    }
}
