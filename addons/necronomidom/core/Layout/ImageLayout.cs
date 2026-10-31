using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public readonly record struct ImageResource(string Key, double Width, double Height);
public readonly record struct ImageSize(double Width, double Height);
public interface IImageSource { ImageResource Load(Element element); }

/// <summary>CSS Images concrete object sizing and positioning. Percentages use signed free space, never the box size.</summary>
public static class ImageLayout
{
    public static ImageResource Intrinsic(IImageSource? source, Element element)
    {
        var image = (source ?? throw new NotSupportedException("img requires an image source adapter.")).Load(element);
        if (!double.IsFinite(image.Width) || !double.IsFinite(image.Height) || image.Width <= 0 || image.Height <= 0)
            throw new InvalidOperationException("Invalid intrinsic image dimensions: " + image.Key);
        return image;
    }
    public static ImageSize Size(CSSStyleDeclaration style, ImageResource image, double available, double horizontalExtra, double verticalExtra, double? definiteHeightBasis = null)
    {
        var w = Value(style, "width"); var h = Value(style, "height");
        bool autoW = w is CssKeywordValue { Value: "auto" };
        bool autoH = h is CssKeywordValue { Value: "auto" } || !definiteHeightBasis.HasValue && HasPercentage(h);
        bool borderBox = style.GetPropertyValue("box-sizing") == "border-box";
        double width = autoW ? image.Width : System.Math.Max(0, Length(w, available) - (borderBox ? horizontalExtra : 0));
        double height = autoH ? image.Height : System.Math.Max(0, Length(h, definiteHeightBasis ?? 0) - (borderBox ? verticalExtra : 0));
        if (autoW && !autoH) width = height * image.Width / image.Height;
        else if (!autoW && autoH) height = width * image.Height / image.Width;
        return new ImageSize(width, height);
    }

    private static bool HasPercentage(CssValue value) => value is CssPercentageValue ||
        value is CssMathValue math && HasPercentage(math.Root);

    private static bool HasPercentage(CssMathExpression expression)
    {
        if (expression is CssMathLiteral { Unit: "%" }) return true;
        if (expression is CssMathNumericLeaf leaf) return HasPercentage(leaf.Value) || leaf.Type.Percentage != 0;
        if (expression is CssMathOperation operation)
            for (int i = 0; i < operation.Arguments.Count; i++)
                if (HasPercentage(operation.Arguments[i])) return true;
        return false;
    }
    public static ImageRun? Paint(CSSStyleDeclaration style, ImageResource image, Rect content)
    {
        if (content.Width <= 0 || content.Height <= 0) return null;
        double width = image.Width, height = image.Height;
        double contain = System.Math.Min(content.Width / width, content.Height / height);
        double scale;
        switch (style.GetPropertyValue("object-fit"))
        {
            case "fill": width = content.Width; height = content.Height; break;
            case "contain": scale = contain; width *= scale; height *= scale; break;
            case "cover": scale = System.Math.Max(content.Width / width, content.Height / height); width *= scale; height *= scale; break;
            case "none": break;
            case "scale-down":
                scale = System.Math.Min(1, contain); width *= scale; height *= scale; break;
            case "cover scale-down":
                scale = System.Math.Min(1, System.Math.Max(content.Width / width, content.Height / height)); width *= scale; height *= scale; break;
            default: throw new NotSupportedException("Unsupported object-fit: " + style.GetPropertyValue("object-fit"));
        }
        if (Value(style, "object-position") is not CssListValue { Items.Count: 2 } position)
            throw new NotSupportedException("object-position requires a computed two-axis value.");
        ImageSampling sampling = style.GetPropertyValue("image-rendering") switch
        {
            "auto" or "smooth" or "high-quality" => ImageSampling.Linear,
            "crisp-edges" => ImageSampling.Nearest,
            "pixelated" => ImageSampling.Pixelated,
            var value => throw new NotSupportedException("Unsupported image-rendering: " + value)
        };
        return Place(image, content, content, new ImageSize(width, height),
            position.Items[0], position.Items[1], sampling);
    }
    /// <summary>Places one concrete image tile in signed free space and crops it to its CSS painting area.</summary>
    public static ImageRun? Place(ImageResource image, Rect positioningArea, Rect clip, ImageSize size,
        CssValue horizontalPosition, CssValue verticalPosition, ImageSampling sampling)
    {
        if (size.Width <= 0 || size.Height <= 0 || clip.Width <= 0 || clip.Height <= 0) return null;
        double x = positioningArea.X + Length(horizontalPosition, positioningArea.Width - size.Width);
        double y = positioningArea.Y + Length(verticalPosition, positioningArea.Height - size.Height);
        double left = System.Math.Max(clip.X, x), top = System.Math.Max(clip.Y, y);
        double right = System.Math.Min(clip.X + clip.Width, x + size.Width);
        double bottom = System.Math.Min(clip.Y + clip.Height, y + size.Height);
        if (right <= left || bottom <= top) return null;
        var source = new Rect((left - x) * image.Width / size.Width, (top - y) * image.Height / size.Height,
            (right - left) * image.Width / size.Width, (bottom - top) * image.Height / size.Height);
        return new ImageRun(image.Key, new Rect(left, top, right - left, bottom - top), source, sampling);
    }
    /// <summary>Raster bounds round edges, not size independently; CSS layout remains subpixel.</summary>
    public static Rect RasterBounds(Rect bounds)
    {
        double left = System.Math.Round(bounds.X, MidpointRounding.AwayFromZero);
        double top = System.Math.Round(bounds.Y, MidpointRounding.AwayFromZero);
        double right = System.Math.Round(bounds.X + bounds.Width, MidpointRounding.AwayFromZero);
        double bottom = System.Math.Round(bounds.Y + bounds.Height, MidpointRounding.AwayFromZero);
        return new Rect(left, top, right - left, bottom - top);
    }
    private static CssValue Value(CSSStyleDeclaration style, string name)
        => style.GetSpecifiedValue(name) ?? throw new InvalidOperationException("Missing computed image property: " + name);
    public static double Length(CssValue value, double basis)
    {
        double result;
        switch (value)
        {
            case CssDimensionValue { Unit: "px" } px: result = px.Value; break;
            case CssPercentageValue percent: result = percent.Value * basis / 100; break;
            case CssNumberValue { Value: 0 }: result = 0; break;
            case CssMathValue math when math.TryEvaluate(new CssMathEvaluationContext(percentageBasis: basis, percentageHint: CssNumericDimension.Length), out result): break;
            default: throw new NotSupportedException("Unresolved image length: " + value.Serialize());
        }
        if (!double.IsFinite(result)) throw new NotSupportedException("Nonfinite image length.");
        return result;
    }
}
