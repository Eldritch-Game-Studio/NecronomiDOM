using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly IAnimatedWidthSource? _animatedWidth;

    private AnimatedWidth? SampleWidth(Element element)
    {
        if (_animatedWidth == null || !_animatedWidth.TryGetWidth(element, out var width)) return null;
        if (element.LocalName == "img")
            throw new NotSupportedException("Animated width on replaced images is outside the current geometry profile.");
        return width;
    }

    private bool WidthIsAuto(Element element, CSSStyleDeclaration style)
        => !SampleWidth(element).HasValue && style.GetPropertyValue("width") == "auto";

    private bool WidthHasPercentage(Element element, CSSStyleDeclaration style)
        => SampleWidth(element) is { } sampled ? sampled.HasPercentage : style.GetPropertyValue("width").EndsWith('%');

    private double UsedWidth(Element element, CSSStyleDeclaration style, double basis)
        => SampleWidth(element)?.Resolve(basis) ?? Length(style, "width", basis);

    private double? IntrinsicSpecifiedWidth(Element element, CSSStyleDeclaration style)
    {
        if (SampleWidth(element) is { } sampled)
            return sampled.HasPercentage ? null : sampled.Pixels;
        string width = style.GetPropertyValue("width");
        return width == "auto" || width.EndsWith('%') ? null : Number(width, 0);
    }
}
