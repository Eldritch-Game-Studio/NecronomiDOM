using System;

using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade
{
    public static partial class ComputedValueResolver
    {
        private static CssValue Transform(CssValue value)
        {
            if (value is CssKeywordValue { Value: "none" }) return value;
            Span<double> matrix = stackalloc double[16];
            Identity(matrix);
            Span<double> next = stackalloc double[16];
            var items = Items(value);
            for (int i = 0; i < items.Count; i++)
            {
                CssValue item = items[i];
                if (item is not CssFunctionValue function) return value;
                if (!TransformFunction(function, next)) return value;
                Multiply(matrix, next);
            }
            bool flat = matrix[2] == 0 && matrix[3] == 0 && matrix[6] == 0 && matrix[7] == 0 &&
                matrix[8] == 0 && matrix[9] == 0 && matrix[10] == 1 && matrix[11] == 0 && matrix[14] == 0 && matrix[15] == 1;
            var output = new CssValue[flat ? 6 : 16];
            if (flat)
            {
                output[0] = Number(matrix[0]); output[1] = Number(matrix[1]); output[2] = Number(matrix[4]);
                output[3] = Number(matrix[5]); output[4] = Number(matrix[12]); output[5] = Number(matrix[13]);
            }
            else for (int i = 0; i < 16; i++) output[i] = Number(matrix[i]);
            return new CssFunctionValue(flat ? "matrix" : "matrix3d", new CssListValue(output, CssListSeparator.Comma));
        }

        private static CssNumberValue Number(double number) => new(System.Math.Abs(number) < 1e-14 ? 0 : number, false);
        private static void Identity(Span<double> matrix)
        {
            matrix.Clear(); matrix[0] = matrix[5] = matrix[10] = matrix[15] = 1;
        }
        private static void Multiply(Span<double> matrix, ReadOnlySpan<double> next)
        {
            Span<double> result = stackalloc double[16];
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 4; row++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) sum += matrix[k * 4 + row] * next[column * 4 + k];
                    result[column * 4 + row] = sum;
                }
            result.CopyTo(matrix);
        }

        private static bool TransformFunction(CssFunctionValue function, Span<double> matrix)
        {
            Identity(matrix);
            var arguments = function.Arguments is CssListValue list ? new ValueItems(list.Items)
                : function.Arguments == null ? new ValueItems(Array.Empty<CssValue>()) : Items(function.Arguments);
            if (function.Name == "perspective" && arguments.Count == 1 && arguments[0] is CssKeywordValue { Value: "none" }) return true;
            Span<double> numbers = stackalloc double[16];
            if (arguments.Count > numbers.Length) return false;
            for (int i = 0; i < arguments.Count; i++)
            {
                switch (arguments[i])
                {
                    case CssNumberValue number: numbers[i] = number.Value; break;
                    case CssDimensionValue { Unit: "px" or "deg" } dimension: numbers[i] = dimension.Value; break;
                    case CssPercentageValue percentage when function.Name.StartsWith("scale", StringComparison.Ordinal): numbers[i] = percentage.Value / 100; break;
                    default: return false;
                }
            }
            int count = arguments.Count;
            double first = count == 0 ? 0 : numbers[0];
            switch (function.Name)
            {
                case "matrix" when count == 6:
                    matrix[0] = numbers[0]; matrix[1] = numbers[1]; matrix[4] = numbers[2]; matrix[5] = numbers[3]; matrix[12] = numbers[4]; matrix[13] = numbers[5]; return true;
                case "matrix3d" when count == 16: numbers.CopyTo(matrix); return true;
                case "translate" when count is 1 or 2: matrix[12] = first; matrix[13] = count == 2 ? numbers[1] : 0; return true;
                case "translate3d" when count == 3: matrix[12] = first; matrix[13] = numbers[1]; matrix[14] = numbers[2]; return true;
                case "translatex" or "translateX" when count == 1: matrix[12] = first; return true;
                case "translatey" or "translateY" when count == 1: matrix[13] = first; return true;
                case "translatez" or "translateZ" when count == 1: matrix[14] = first; return true;
                case "scale" when count is 1 or 2: matrix[0] = first; matrix[5] = count == 2 ? numbers[1] : first; return true;
                case "scale3d" when count == 3: matrix[0] = first; matrix[5] = numbers[1]; matrix[10] = numbers[2]; return true;
                case "scalex" or "scaleX" when count == 1: matrix[0] = first; return true;
                case "scaley" or "scaleY" when count == 1: matrix[5] = first; return true;
                case "scalez" or "scaleZ" when count == 1: matrix[10] = first; return true;
                case "rotate" or "rotatez" or "rotateZ" when count == 1: Rotation(matrix, 0, 0, 1, first); return true;
                case "rotatex" or "rotateX" when count == 1: Rotation(matrix, 1, 0, 0, first); return true;
                case "rotatey" or "rotateY" when count == 1: Rotation(matrix, 0, 1, 0, first); return true;
                case "rotate3d" when count == 4: Rotation(matrix, first, numbers[1], numbers[2], numbers[3]); return true;
                case "skew" when count is 1 or 2: matrix[4] = System.Math.Tan(Radians(first)); matrix[1] = count == 2 ? System.Math.Tan(Radians(numbers[1])) : 0; return true;
                case "skewx" or "skewX" when count == 1: matrix[4] = System.Math.Tan(Radians(first)); return true;
                case "skewy" or "skewY" when count == 1: matrix[1] = System.Math.Tan(Radians(first)); return true;
                case "perspective" when count == 1: matrix[11] = -1 / System.Math.Max(1, first); return true;
                default: return false;
            }
        }

        private static void Rotation(Span<double> matrix, double x, double y, double z, double degrees)
        {
            double length = System.Math.Sqrt(x * x + y * y + z * z);
            if (length == 0) return;
            x /= length; y /= length; z /= length;
            double radians = Radians(degrees), c = System.Math.Cos(radians), s = System.Math.Sin(radians), t = 1 - c;
            matrix[0] = x * x * t + c; matrix[1] = y * x * t + z * s; matrix[2] = z * x * t - y * s;
            matrix[4] = x * y * t - z * s; matrix[5] = y * y * t + c; matrix[6] = z * y * t + x * s;
            matrix[8] = x * z * t + y * s; matrix[9] = y * z * t - x * s; matrix[10] = z * z * t + c;
        }
        private static double Radians(double degrees) => (degrees % 360) * (System.Math.PI / 180);


    }
}
