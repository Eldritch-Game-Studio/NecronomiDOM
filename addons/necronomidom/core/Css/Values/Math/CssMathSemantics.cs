using System;
using M = System.Math;

namespace NecronomiDom.Css.Values.Math
{
    internal static class CssMathSemantics
    {
        internal static double LineWidth(ReadOnlySpan<double> arguments, double devicePixelRatio)
        {
            foreach (double argument in arguments) if (double.IsNaN(argument)) return double.NaN;
            double value = arguments[0];
            if (arguments.Length == 2)
            {
                double interval = M.Abs(arguments[1]);
                if (interval == 0 || double.IsInfinity(value) && double.IsInfinity(interval)) return double.NaN;
                if (double.IsFinite(value) && double.IsFinite(interval) && value != 0)
                {
                    double lower = M.Floor(value / interval) * interval;
                    double upper = M.Ceiling(value / interval) * interval;
                    value = lower == 0 ? upper : upper == 0 ? lower : Round(value, interval, "nearest");
                }
                else value = Round(value, interval, "nearest");
            }
            if (!double.IsFinite(value) || value == 0) return value;
            double pixels = value * devicePixelRatio;
            return M.CopySign(M.Abs(pixels) < 1 ? 1 : M.Truncate(M.Abs(pixels)), pixels) / devicePixelRatio;
        }
        internal static double Evaluate(string name, ReadOnlySpan<double> a, CssNumericType input, string? strategy)
        {
            foreach (double value in a) if (double.IsNaN(value)) return double.NaN;
            double x = a[0], y = a.Length > 1 ? a[1] : 0;
            return name switch
            {
                "+" => Sum(a), "-" => x - y, "*" => Product(a), "/" => x / y,
                "negate" => -x, "invert" => 1 / x,
                "min" => Extremum(a, false), "max" => Extremum(a, true),
                "clamp" => Max(x, Min(y, a[2])),
                "round" => Round(x, y, strategy), "mod" => Mod(x, y, false), "rem" => Mod(x, y, true),
                "abs" => M.Abs(x), "sign" => x == 0 ? x : M.CopySign(1, x),
                "sin" or "cos" or "tan" => Trig(name, x, input.SamePowers(CssNumericType.For(CssNumericDimension.Angle))),
                "asin" => M.Asin(x) * 180 / M.PI, "acos" => M.Acos(x) * 180 / M.PI,
                "atan" => M.Atan(x) * 180 / M.PI, "atan2" => M.Atan2(x, y) * 180 / M.PI,
                "pow" => Pow(x, y), "sqrt" => M.Sqrt(x), "hypot" => Hypot(a),
                "log" => Log(x, a.Length > 1 ? y : M.E), "exp" => M.Exp(x),
                _ => throw new InvalidOperationException("Unknown typed math operator: " + name)
            };
        }
        private static double Sum(ReadOnlySpan<double> a)
        { double result = a[0]; for (int i = 1; i < a.Length; i++) result += a[i]; return result; }
        private static double Product(ReadOnlySpan<double> a)
        { double result = a[0]; for (int i = 1; i < a.Length; i++) result *= a[i]; return result; }
        private static double Extremum(ReadOnlySpan<double> a, bool maximum)
        { double result = a[0]; for (int i = 1; i < a.Length; i++) result = maximum ? Max(result, a[i]) : Min(result, a[i]); return result; }
        private static double Min(double a, double b) => a == 0 && b == 0 ? (double.IsNegative(a) || double.IsNegative(b) ? -0.0 : 0.0) : M.Min(a, b);
        private static double Max(double a, double b) => a == 0 && b == 0 ? (double.IsNegative(a) && double.IsNegative(b) ? -0.0 : 0.0) : M.Max(a, b);
        private static double Round(double a, double b, string? strategy)
        {
            b = M.Abs(b);
            if (b == 0 || double.IsInfinity(a) && double.IsInfinity(b)) return double.NaN;
            if (double.IsInfinity(a)) return a;
            if (double.IsInfinity(b))
            {
                if (strategy == "up" && a > 0) return double.PositiveInfinity;
                if (strategy == "down" && a < 0) return double.NegativeInfinity;
                if (strategy == "up") return a == 0 && !double.IsNegative(a) ? 0 : -0.0;
                if (strategy == "down") return a == 0 && double.IsNegative(a) ? -0.0 : 0;
                return M.CopySign(0, a);
            }
            double quotient = a / b;
            double lower = M.Floor(quotient), upper = M.Ceiling(quotient);
            if (lower == upper) return a;
            double selected = strategy switch
            {
                "up" => upper, "down" => lower, "to-zero" => M.Truncate(quotient),
                _ => quotient - lower < upper - quotient ? lower : upper
            };
            return selected == 0 ? M.CopySign(0, a) : selected * b;
        }
        private static double Mod(double a, double b, bool remainder)
        {
            if (b == 0 || double.IsInfinity(a)) return double.NaN;
            if (double.IsInfinity(b))
                return !remainder && double.IsNegative(a) != double.IsNegative(b) ? double.NaN : a;
            double value = a % b;
            if (remainder) return value;
            if (value == 0) return M.CopySign(0, b);
            return double.IsNegative(value) != double.IsNegative(b) ? value + b : value;
        }
        private static double Trig(string name, double value, bool angle)
        {
            if (double.IsInfinity(value)) return double.NaN;
            if (angle)
            {
                double degrees = value % 360;
                if (degrees % 90 == 0)
                {
                    int quarter = (int)(degrees / 90);
                    return name switch
                    {
                        "sin" => quarter is 1 or -3 ? 1 : quarter is -1 or 3 ? -1 : M.CopySign(0, value),
                        "cos" => quarter == 0 ? 1 : quarter is 2 or -2 ? -1 : 0,
                        _ => quarter is 1 or -3 ? double.PositiveInfinity : quarter is -1 or 3 ? double.NegativeInfinity : M.CopySign(0, value)
                    };
                }
                value = degrees * M.PI / 180;
            }
            return name switch { "sin" => M.Sin(value), "cos" => M.Cos(value), _ => M.Tan(value) };
        }
        private static double Pow(double x, double y)
        {
            if (M.Abs(x) == 1 && double.IsInfinity(y)) return double.NaN;
            return M.Pow(x, y);
        }
        private static double Log(double x, double basis)
        {
            if (basis < 0 || basis == 1 || x < 0) return double.NaN;
            if (x == 0) return double.NegativeInfinity;
            if (x == 1) return 0;
            if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
            return M.Log(x) / M.Log(basis);
        }
        private static double Hypot(ReadOnlySpan<double> values)
        {
            double maximum = 0;
            foreach (double value in values) maximum = M.Max(maximum, M.Abs(value));
            if (maximum == 0 || double.IsInfinity(maximum)) return maximum;
            double sum = 0;
            foreach (double value in values) { double scaled = value / maximum; sum += scaled * scaled; }
            return maximum * M.Sqrt(sum);
        }
    }
}
