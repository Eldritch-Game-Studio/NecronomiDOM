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

    private bool WidthIsAuto(Element element, BoxStyleSnapshot style)
        => !SampleWidth(element).HasValue && style.Width.IsAuto;

    private bool WidthHasPercentage(Element element, BoxStyleSnapshot style)
        => SampleWidth(element) is { } sampled ? sampled.HasPercentage : style.Width.IsPercent;

    private double UsedWidth(Element element, BoxStyleSnapshot style, double basis)
        => SampleWidth(element)?.Resolve(basis) ?? Length(style, style.Width, "width", basis);

    private double? IntrinsicSpecifiedWidth(Element element, BoxStyleSnapshot style)
    {
        if (SampleWidth(element) is { } sampled)
            return sampled.HasPercentage ? null : sampled.Pixels;
        var width = style.Width;
        return width.IsAuto || width.IsPercent ? null : Length(style, width, "width", 0);
    }
}
