using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    // Single nonrepeating URL tiles keep the original path; generated or layered backgrounds
    // use typed per-layer origin/size/position/repeat/clip values and the same actual URL source.
    private bool PaintBackgroundDecorations(Element element, CSSStyleDeclaration values, PositionedBoxResult box,
        in BoxDecorationParts decoration, ref int contentStart)
    {
        CssValue image = BackgroundValue(values, "background-image");
        if (image is CssGradientValue || image is CssListValue { Separator: CssListSeparator.Comma })
            return PaintLayeredBackgroundDecorations(element, values, image, box, in decoration, ref contentStart);
        bool active = image is not CssKeywordValue { Value: "none" };
        string clipEdge = BackgroundKeyword(values, "background-clip");
        if (!active && (clipEdge == "border-box" || decoration.Background.A == 0)) return false;
        var clip = BackgroundEdge(clipEdge, box, in decoration);
        if (decoration.Background.A > 0 && !clip.IsEmpty)
        {
            PaintRun color = UniformCircularCorners(clip.Radii, out double radius)
                ? new BoxRun(clip.Bounds, new BoxStyle(decoration.Background, default, 0, radius))
                : new BorderBoxRun(clip.Bounds, decoration.Background, default, clip.Radii);
            _result.Runs.Insert(contentStart++, color);
        }
        if (active && !clip.IsEmpty)
        {
            if (image is not CssUrlValue url)
                throw new NotSupportedException("Runtime background images require one computed URL tile.");
            if (element.LocalName is "html" or "body")
                throw new NotSupportedException("Root/body canvas background image propagation is not yet supported.");
            if (!NoRepeat(BackgroundLayer(BackgroundValue(values, "background-repeat"))))
                throw new NotSupportedException("Runtime background images currently require no-repeat on both axes.");
            Rect origin = BackgroundEdge(BackgroundKeyword(values, "background-origin"), box, in decoration).Bounds;
            ImageSampling sampling = BackgroundKeyword(values, "image-rendering") switch
            {
                "auto" or "smooth" or "high-quality" => ImageSampling.Linear,
                "crisp-edges" => ImageSampling.Nearest, "pixelated" => ImageSampling.Pixelated,
                var unsupported => throw new NotSupportedException("Unsupported background image sampling: " + unsupported)
            };
            PaintUrlBackgroundTile(element, url, origin, in clip,
                BackgroundLayer(BackgroundValue(values, "background-size")),
                BackgroundLayer(BackgroundValue(values, "background-position-x")),
                BackgroundLayer(BackgroundValue(values, "background-position-y")), sampling, ref contentStart);
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
    /// <summary>Uses the existing allowed-resource cache and placement law for a typed URL background tile.</summary>
    private void PaintUrlBackgroundTile(Element element, CssUrlValue url, Rect origin, in EllipticalClipGeometry clip,
        CssValue size, CssValue positionX, CssValue positionY, ImageSampling sampling, ref int contentStart)
    {
        var source = _images as IBackgroundImageSource
            ?? throw new NotSupportedException("Background images require the native background source adapter.");
        // The computed URL already retains its document-relative provenance; do not serialize or resolve it again.
        var resource = source.LoadBackground(element, url.Url);
        if (!double.IsFinite(resource.Width) || !double.IsFinite(resource.Height) || resource.Width <= 0 || resource.Height <= 0)
            throw new InvalidOperationException("Invalid intrinsic background image: " + resource.Key);
        ImageSize usedSize = BackgroundSize(size, resource, origin);
        if (ImageLayout.Place(resource, origin, clip.Bounds, usedSize, positionX, positionY, sampling) is { } tile)
            _result.Runs.Insert(contentStart++, new BackgroundImageRun(tile, clip));
    }
    private static CssValue BackgroundValue(CSSStyleDeclaration values, string property) => values.GetSpecifiedValue(property)
        ?? throw new InvalidOperationException("Missing computed background property: " + property);
    private static CssValue BackgroundLayer(CssValue value) => value is CssListValue { Separator: CssListSeparator.Comma } list
        ? list.Items[0] : value;
    private static string BackgroundKeyword(CSSStyleDeclaration values, string property) =>
        BackgroundLayer(BackgroundValue(values, property)) is CssKeywordValue keyword ? keyword.Value
        : throw new NotSupportedException("Runtime background keyword is unresolved: " + property);
    private static bool NoRepeat(CssValue value) => value is CssKeywordValue { Value: "no-repeat" }
        || value is CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } pair
            && pair.Items[0] is CssKeywordValue { Value: "no-repeat" } && pair.Items[1] is CssKeywordValue { Value: "no-repeat" };
    private static EllipticalClipGeometry BackgroundEdge(string edge, PositionedBoxResult box, in BoxDecorationParts decoration)
    {
        Rect bounds = edge switch
        {
            "border-box" => box.BorderBox, "padding-box" => box.PaddingBox, "content-box" => box.ContentBox,
            _ => throw new NotSupportedException("Unsupported background clipping/positioning edge: " + edge)
        };
        if (edge == "border-box") return new(bounds, decoration.Radii);
        var outer = box.BorderBox;
        var insets = new BorderSides(new(bounds.Y - outer.Y, default, "none"),
            new(outer.X + outer.Width - bounds.X - bounds.Width, default, "none"),
            new(outer.Y + outer.Height - bounds.Y - bounds.Height, default, "none"),
            new(bounds.X - outer.X, default, "none"));
        return new(bounds, decoration.Radii.Normalize(outer.Width, outer.Height).Inner(insets));
    }
    private static ImageSize BackgroundSize(CssValue value, ImageResource image, Rect origin)
    {
        if (value is CssKeywordValue { Value: "contain" or "cover" } fit)
        {
            double scale = fit.Value == "contain" ? Math.Min(origin.Width / image.Width, origin.Height / image.Height)
                : Math.Max(origin.Width / image.Width, origin.Height / image.Height);
            return new(image.Width * scale, image.Height * scale);
        }
        if (value is not CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } dimensions)
            throw new NotSupportedException("Runtime background-size requires computed contain/cover or two axes.");
        bool autoWidth = dimensions.Items[0] is CssKeywordValue { Value: "auto" };
        bool autoHeight = dimensions.Items[1] is CssKeywordValue { Value: "auto" };
        double width = autoWidth ? image.Width : ImageLayout.Length(dimensions.Items[0], origin.Width);
        double height = autoHeight ? image.Height : ImageLayout.Length(dimensions.Items[1], origin.Height);
        if (autoWidth && !autoHeight) width = height * image.Width / image.Height;
        else if (!autoWidth && autoHeight) height = width * image.Height / image.Width;
        return new(width, height);
    }
}
