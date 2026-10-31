using System.Collections.Generic;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values
{
    // CSS Easing 2 § 2.1: normalize fully resolved output values and monotonically positioned linear stops.
    internal static class CssLinearEasingValues
    {
        internal static CssValue Specified(CssValue value)
        {
            if (value is not CssFunctionValue { Name: "linear", Arguments: not null } function) return value;
            // Specified CSSOM retains omitted/two-position stops and simplified calc() values.
            // Non-finite math is canonicalized numerically, as required by the WPT exceptional-value serialization.
            if (!HasExceptionalMath(function.Arguments) || !Resolve(function.Arguments, out CssValue arguments)) return value;
            return Normalize(new CssFunctionValue("linear", arguments));
        }

        private static bool HasExceptionalMath(CssValue value)
        {
            if (value is CssNumberValue number) return !double.IsFinite(number.Value);
            if (value is CssMathValue math)
            {
                var context = math.Fits("percentage") ? CssMathEvaluationContext.PercentageMagnitudes : CssMathEvaluationContext.Specified;
                return math.TryEvaluate(context, out double result) && !double.IsFinite(result);
            }
            if (value is CssListValue list)
                for (int i = 0; i < list.Items.Count; i++)
                    if (HasExceptionalMath(list.Items[i])) return true;
            return false;
        }

        private static bool Resolve(CssValue value, out CssValue resolved)
        {
            resolved = value;
            if (value is CssNumberValue or CssPercentageValue) return true;
            if (value is CssMathValue math)
            {
                bool percentage = math.Fits("percentage");
                if (!percentage && !math.Fits("number")) return false;
                CssMathEvaluationContext context = percentage ? CssMathEvaluationContext.PercentageMagnitudes : CssMathEvaluationContext.Specified;
                if (!math.TryEvaluate(context, out double number)) return false;
                number = double.IsNaN(number) ? 0 : CssNumericRange.Literal(number);
                resolved = percentage ? new CssPercentageValue(number) : new CssNumberValue(number, false);
                return true;
            }
            if (value is not CssListValue list) return false;
            CssValue[]? items = null;
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (!Resolve(list.Items[i], out CssValue item)) return false;
                if (items == null && !ReferenceEquals(item, list.Items[i]))
                {
                    items = new CssValue[list.Items.Count];
                    for (int j = 0; j < i; j++) items[j] = list.Items[j];
                }
                if (items != null) items[i] = item;
            }
            if (items != null) resolved = new CssListValue(items, list.Separator);
            return true;
        }

        internal static CssValue Normalize(CssValue value)
        {
            if (value is not CssFunctionValue { Name: "linear", Arguments: CssListValue arguments }) return value;
            var outputs = new List<CssValue>(arguments.Items.Count * 2);
            var inputs = new List<double?>(arguments.Items.Count * 2);
            foreach (CssValue argument in arguments.Items)
            {
                IReadOnlyList<CssValue> stop = argument is CssListValue list ? list.Items : new[] { argument };
                if (stop.Count == 0 || stop[0] is not CssNumberValue number) return value;
                CssValue output = double.IsNaN(number.Value) ? new CssNumberValue(0, false)
                    : double.IsInfinity(number.Value) ? new CssNumberValue(CssNumericRange.Literal(number.Value), false) : number;
                outputs.Add(output);
                if (stop.Count == 1) inputs.Add(null);
                else if (stop[1] is CssPercentageValue first) inputs.Add(first.Value);
                else return value;
                if (stop.Count == 3)
                {
                    if (stop[2] is not CssPercentageValue second) return value;
                    outputs.Add(output); inputs.Add(second.Value);
                }
            }
            if (outputs.Count < 2) return value;
            inputs[0] ??= 0;
            double largest = inputs[0]!.Value;
            for (int i = 1; i < inputs.Count; i++)
            {
                if (!inputs[i].HasValue) continue;
                largest = System.Math.Max(largest, inputs[i]!.Value);
                inputs[i] = largest;
            }
            inputs[^1] ??= System.Math.Max(100, largest);
            for (int i = 0; i < inputs.Count - 1; i++)
            {
                int next = i + 1;
                while (!inputs[next].HasValue) next++;
                double start = inputs[i]!.Value, end = inputs[next]!.Value;
                for (int j = i + 1; j < next; j++) inputs[j] = start + (end - start) * (j - i) / (next - i);
                i = next - 1;
            }
            var computed = new CssValue[outputs.Count];
            for (int i = 0; i < computed.Length; i++)
                computed[i] = new CssListValue(new[] { outputs[i], new CssPercentageValue(inputs[i]!.Value) }, CssListSeparator.Space);
            return new CssFunctionValue("linear", new CssListValue(computed, CssListSeparator.Comma));
        }
    }
}
