using System;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    // This is an immediate serialization-equivalence check, not a graph/value cache. Mutable children
    // are read again on every validation; unknown value classes retain the existing serializer fallback.
    internal static bool ProfileValuesSerializeIdentically(CssValue? first, CssValue? second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first == null || second == null || first.GetType() != second.GetType()) return false;
        Type type = first.GetType();
        if (type == typeof(CssListValue))
        {
            var left = (CssListValue)first; var right = (CssListValue)second;
            if (left.Separator != right.Separator || left.Items.Count != right.Items.Count) return false;
            for (int i = 0; i < left.Items.Count; i++)
                if (!ProfileValuesSerializeIdentically(left.Items[i], right.Items[i])) return false;
            return true;
        }
        return first switch
        {
            CssKeywordValue keyword => keyword.Value == ((CssKeywordValue)second).Value,
            CssStringValue text => text.Value == ((CssStringValue)second).Value,
            CssDelimValue delimiter => delimiter.Value == ((CssDelimValue)second).Value,
            CssNumberValue number when type == typeof(CssNumberValue) || type == typeof(CssIntegerValue) =>
                SameProfileNumber(number.Value, ((CssNumberValue)second).Value) && number.IsInteger == ((CssNumberValue)second).IsInteger,
            CssDimensionValue dimension when type == typeof(CssDimensionValue) =>
                dimension.Unit == ((CssDimensionValue)second).Unit && SameProfileNumber(dimension.Value, ((CssDimensionValue)second).Value),
            CssPercentageValue percentage when type == typeof(CssPercentageValue) =>
                SameProfileNumber(percentage.Value, ((CssPercentageValue)second).Value),
            CssFunctionValue function => function.Name == ((CssFunctionValue)second).Name &&
                ProfileValuesSerializeIdentically(function.Arguments, ((CssFunctionValue)second).Arguments),
            CssBlockValue block => block.Opener == ((CssBlockValue)second).Opener &&
                ProfileValuesSerializeIdentically(block.Content, ((CssBlockValue)second).Content),
            _ => false
        };
    }
    private static bool SameProfileNumber(double first, double second) =>
        BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);
}
