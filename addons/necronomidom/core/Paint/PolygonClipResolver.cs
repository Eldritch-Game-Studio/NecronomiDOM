using System;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Paint;

/// <summary>Used convex CSS polygon geometry; percentages resolve against the final border box.</summary>
public static class PolygonClipResolver
{
    public static ConvexPolygon Resolve(CssValue value, Rect referenceBounds)
    {
        PaintGeometryValidation.Bounds(referenceBounds);
        ArgumentNullException.ThrowIfNull(value);
        // The default geometry box for clip-path is border-box. Other boxes require their own used rect.
        if (value is CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } boxed)
        {
            if (boxed.Items[0] is CssKeywordValue { Value: "border-box" }) value = boxed.Items[1];
            else if (boxed.Items[1] is CssKeywordValue { Value: "border-box" }) value = boxed.Items[0];
            else throw new NotSupportedException("Polygon paint currently requires the border-box reference geometry.");
        }
        if (value is not CssFunctionValue { Name: "polygon", Arguments: CssListValue { Separator: CssListSeparator.Comma } coordinates })
            throw new NotSupportedException("Polygon paint requires a computed CSS polygon() shape.");
        if (coordinates.Items.Count == 0) throw new NotSupportedException("A CSS polygon requires vertices.");
        int start = coordinates.Items[0] is CssKeywordValue { Value: "nonzero" or "evenodd" } ? 1 : 0;
        // The property grammar packs its optional fill-rule comma separately
        // from the repeated coordinate list; retain that typed tree, without
        // reparsing strings or copying the vertices.
        if (start == 1 && coordinates.Items.Count == 2
            && coordinates.Items[1] is CssListValue { Separator: CssListSeparator.Comma } vertices)
        {
            coordinates = vertices;
            start = 0;
        }
        var points = new PaintPoint[coordinates.Items.Count - start];
        for (int i = start; i < coordinates.Items.Count; i++)
        {
            if (coordinates.Items[i] is not CssListValue { Separator: CssListSeparator.Space, Items.Count: 2 } pair)
                throw new NotSupportedException("A CSS polygon vertex requires two computed coordinates.");
            points[i - start] = new PaintPoint(Coordinate(pair.Items[0], referenceBounds.Width), Coordinate(pair.Items[1], referenceBounds.Height));
        }
        return ConvexPolygon.FromOwned(points);
    }

    private static double Coordinate(CssValue value, double reference) => value switch
    {
        CssDimensionValue { Unit: "px" } pixels => pixels.Value,
        CssPercentageValue percent => percent.Value * reference / 100,
        CssNumberValue { Value: 0 } => 0,
        _ => throw new NotSupportedException("Polygon paint coordinates must compute to finite pixels or percentages.")
    };
}
