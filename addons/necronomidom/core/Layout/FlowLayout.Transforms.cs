using NecronomiDom.Css.Om;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private bool HasTransform(Element element, CSSStyleDeclaration style) => SampleScale(element).HasValue
        || style.GetPropertyValue("transform") != "none" || style.GetPropertyValue("translate") != "none"
        || style.GetPropertyValue("rotate") != "none" || style.GetPropertyValue("scale") != "none";

    // A deferred subtree moves its coordinate frame as well as its transform-origin. Conjugating
    // preserves the authored transform around the translated final CSS border box.
    private static Affine2D TranslateTransform(Affine2D matrix, double dx, double dy) => matrix with
    {
        E = matrix.E + dx - matrix.A * dx - matrix.C * dy,
        F = matrix.F + dy - matrix.B * dx - matrix.D * dy
    };
}
