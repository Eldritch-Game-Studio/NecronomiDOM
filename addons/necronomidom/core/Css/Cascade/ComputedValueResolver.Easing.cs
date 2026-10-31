using System;

using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue Easing(CssValue value)
        {
            if (value is CssFunctionValue { Name: "cubic-bezier", Arguments: CssListValue bezier } && bezier.Items.Count == 4)
            {
                CssValue[]? coordinates = null;
                for (int i = 0; i < 4; i++)
                {
                    CssValue coordinate = bezier.Items[i];
                    if (i is 0 or 2 && coordinate is CssNumberValue number && number.Value is < 0 or > 1)
                        coordinate = new CssNumberValue(System.Math.Clamp(number.Value, 0, 1), false);
                    if (coordinates == null && !ReferenceEquals(coordinate, bezier.Items[i]))
                    {
                        coordinates = new CssValue[4];
                        for (int j = 0; j < i; j++) coordinates[j] = bezier.Items[j];
                    }
                    if (coordinates != null) coordinates[i] = coordinate;
                }
                return coordinates == null ? value : new CssFunctionValue("cubic-bezier", new CssListValue(coordinates, CssListSeparator.Comma));
            }
            if (value is CssFunctionValue { Name: "steps" } steps && steps.Arguments != null)
            {
                var components = steps.Arguments is CssListValue stepList ? new ValueItems(stepList.Items) : Items(steps.Arguments);
                if (components[0] is not CssNumberValue number) return value;
                double minimum = components.Count > 1 && components[1] is CssKeywordValue { Value: "jump-none" } ? 2 : 1;
                double stepCount = System.Math.Max(minimum, System.Math.Floor(number.Value + 0.5));
                if (stepCount == number.Value && number is CssIntegerValue) return value;
                CssValue count = new CssIntegerValue(stepCount);
                return new CssFunctionValue("steps", components.Count == 1 ? count : new CssListValue(new[] { count, components[1] }, CssListSeparator.Comma));
            }
            return CssLinearEasingValues.Normalize(value);
        }
    }
}
