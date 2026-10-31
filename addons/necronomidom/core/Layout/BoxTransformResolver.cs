using System;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>CSS Transforms 2: origin * translate * rotate * scale * transform-list * inverse-origin.</summary>
public static class BoxTransformResolver
{
    /// <param name="style">StyleEngine.GetComputedValues, not CSSOM GetComputedStyle's already-resolved origin/translation view.</param>
    /// <param name="finalBorderBounds">Final untransformed border rectangle in viewport CSS pixels.</param>
    public static UsedBoxTransform Resolve(CSSStyleDeclaration style, Rect finalBorderBounds, AnimatedScale? animatedScale = null)
    {
        var translate = style.GetSpecifiedValue("translate");
        var rotate = style.GetSpecifiedValue("rotate");
        var scale = style.GetSpecifiedValue("scale");
        var transform = style.GetSpecifiedValue("transform");
        bool establishes = animatedScale.HasValue || !None(translate) || !None(rotate) || !None(scale) || !None(transform);
        if (!establishes) return new(Affine2D.Identity, false);
        // HTML layout-box view-box/stroke-box used references are border-box, not SVG viewport boxes.
        if (style.GetSpecifiedValue("transform-box") is CssKeywordValue { Value: "content-box" or "fill-box" })
            throw new NotSupportedException("Content-box transform reference requires final content bounds.");
        var matrix = Affine2D.Identity;
        if (!None(translate)) matrix = Affine2D.Compose(matrix, Translation(translate!, finalBorderBounds));
        if (!None(rotate)) matrix = Affine2D.Compose(matrix, Rotation(rotate!));
        if (animatedScale is { } sampled)
        {
            if (!double.IsFinite(sampled.X) || !double.IsFinite(sampled.Y))
                throw new NotSupportedException("Nonfinite animated individual scale.");
            if (!None(scale) && Count(scale!) > 2 && Number(At(scale!, 2)) != 1)
                throw new NotSupportedException("3D scale is unavailable.");
            matrix = Affine2D.Compose(matrix, new(sampled.X, 0, 0, sampled.Y, 0, 0));
        }
        else if (!None(scale)) matrix = Affine2D.Compose(matrix, Scaling(scale!));
        if (!None(transform))
        {
            if (!TypedTransformDecoder.TryDecode(transform!, out var typed) || !typed.Is2D)
                throw new NotSupportedException("Runtime requires a genuine 2D CSS transform.");
            foreach (var component in typed.Components)
            {
                Affine2D next;
                if (component.Kind == CSSTransformKind.Translate)
                    next = new(1, 0, 0, 1, NumericLength(component.X, finalBorderBounds.Width), NumericLength(component.Y, finalBorderBounds.Height));
                else
                {
                    var m = component.ToMatrix();
                    next = new(m.M11, m.M12, m.M21, m.M22, m.M41, m.M42);
                }
                matrix = Affine2D.Compose(matrix, next);
            }
        }
        CssValue origin = style.GetSpecifiedValue("transform-origin")
            ?? throw new NotSupportedException("Missing computed transform-origin.");
        if (origin is not CssListValue { Items.Count: >= 2 and <= 3 } position)
            throw new NotSupportedException("Runtime requires computed transform-origin coordinates.");
        if (position.Items.Count == 3 && UsedCssNumeric.Length(position.Items[2], 0) != 0)
            throw new NotSupportedException("3D transform-origin is unavailable.");
        double x = finalBorderBounds.X + UsedCssNumeric.Length(position.Items[0], finalBorderBounds.Width);
        double y = finalBorderBounds.Y + UsedCssNumeric.Length(position.Items[1], finalBorderBounds.Height);
        matrix = Affine2D.Compose(new(1, 0, 0, 1, x, y), Affine2D.Compose(matrix, new(1, 0, 0, 1, -x, -y)));
        return new(matrix, true);
    }
    private static bool None(CssValue? value) => value == null || value is CssKeywordValue { Value: "none" };
    private static int Count(CssValue value) => value is CssListValue list ? list.Items.Count : 1;
    private static CssValue At(CssValue value, int index) => value is CssListValue list ? list.Items[index] : index == 0 ? value : throw new NotSupportedException("Missing transform coordinate.");
    private static Affine2D Translation(CssValue value, Rect bounds)
    {
        int count = Count(value);
        if (count > 2 && UsedCssNumeric.Length(At(value, 2), 0) != 0) throw new NotSupportedException("3D translate is unavailable.");
        return new(1, 0, 0, 1, UsedCssNumeric.Length(At(value, 0), bounds.Width), count >= 2 ? UsedCssNumeric.Length(At(value, 1), bounds.Height) : 0);
    }
    private static Affine2D Scaling(CssValue value)
    {
        int count = Count(value);
        double x = Number(At(value, 0)), y = count >= 2 ? Number(At(value, 1)) : x;
        if (count > 2 && Number(At(value, 2)) != 1) throw new NotSupportedException("3D scale is unavailable.");
        return new(x, 0, 0, y, 0, 0);
    }
    private static Affine2D Rotation(CssValue value)
    {
        int count = Count(value);
        CssValue angle = At(value, count - 1);
        if (count == 2 && At(value, 0) is not CssKeywordValue { Value: "z" }) throw new NotSupportedException("3D rotate is unavailable.");
        if (count == 4 && (Number(At(value, 0)) != 0 || Number(At(value, 1)) != 0 || Number(At(value, 2)) == 0))
            throw new NotSupportedException("3D rotate is unavailable.");
        if (!CSSNumericValue.TryFrom(angle, out var numeric)) throw new NotSupportedException("Unresolved rotation angle.");
        double radians = numeric.To("rad").Value;
        if (count == 4 && Number(At(value, 2)) < 0) radians = -radians;
        return new(Math.Cos(radians), Math.Sin(radians), -Math.Sin(radians), Math.Cos(radians), 0, 0);
    }
    private static double Number(CssValue value)
    {
        if (!CSSNumericValue.TryFrom(value, out var numeric)) throw new NotSupportedException("Unresolved scale.");
        double number = numeric.To("number").Value;
        if (!double.IsFinite(number)) throw new NotSupportedException("Nonfinite scale.");
        return number;
    }
    private static double NumericLength(CSSNumericValue value, double basis)
    {
        if (value.TryGetUnit(out var unit))
            return unit.Unit == "percent" ? unit.Value * basis / 100 : unit.To("px").Value;
        if (value.Expression.TryEvaluate(new CssMathEvaluationContext(percentageBasis: basis,
            percentageHint: CssNumericDimension.Length), out double result) && double.IsFinite(result))
            return result;
        throw new NotSupportedException("Unresolved translation.");
    }
}
