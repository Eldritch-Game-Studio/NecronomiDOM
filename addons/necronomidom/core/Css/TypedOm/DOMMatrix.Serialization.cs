using System;
using System.Globalization;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    public partial struct DOMMatrix
    {
        // Geometry stringification uses ECMAScript ToString(double), not CSS's
        // shortened numeric serialization. Preserve shortest round-trip digits,
        // decimal notation for [1e-6, 1e21), and normalized lowercase exponents.
        internal static string SerializeGeometryNumber(double value)
        {
            if (!double.IsFinite(value))
                throw DomException.InvalidState("A nonfinite DOMMatrix cannot be serialized as a CSS transform.");
            if (value == 0) return "0";
            Span<char> raw = stackalloc char[32];
            value.TryFormat(raw, out int length, "R", CultureInfo.InvariantCulture);
            raw = raw[..length];
            bool negative = raw[0] == '-';
            int start = negative ? 1 : 0;
            int exponentIndex = raw.IndexOf('E');
            int end = exponentIndex < 0 ? length : exponentIndex;
            int exponent = exponentIndex < 0 ? 0 : int.Parse(raw[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
            Span<char> digits = stackalloc char[24];
            int count = 0, decimalPosition = -1;
            for (int i = start; i < end; i++)
            {
                if (raw[i] == '.') decimalPosition = count;
                else digits[count++] = raw[i];
            }
            if (decimalPosition < 0) decimalPosition = count;
            decimalPosition += exponent;
            // Fixed notation can include leading zeroes before its first digit.
            int leading = 0;
            while (leading < count - 1 && digits[leading] == '0') leading++;
            if (leading != 0)
            {
                digits[leading..count].CopyTo(digits);
                count -= leading;
                decimalPosition -= leading;
            }
            while (count > 1 && digits[count - 1] == '0') count--;
            Span<char> output = stackalloc char[32];
            int position = 0;
            if (negative) output[position++] = '-';
            if (decimalPosition > 0 && decimalPosition <= 21)
            {
                int beforeDot = Math.Min(decimalPosition, count);
                digits[..beforeDot].CopyTo(output[position..]); position += beforeDot;
                for (int i = count; i < decimalPosition; i++) output[position++] = '0';
                if (count > decimalPosition)
                {
                    output[position++] = '.';
                    digits[decimalPosition..count].CopyTo(output[position..]); position += count - decimalPosition;
                }
            }
            else if (decimalPosition <= 0 && decimalPosition > -6)
            {
                output[position++] = '0'; output[position++] = '.';
                for (int i = 0; i < -decimalPosition; i++) output[position++] = '0';
                digits[..count].CopyTo(output[position..]); position += count;
            }
            else
            {
                output[position++] = digits[0];
                if (count > 1)
                {
                    output[position++] = '.';
                    digits[1..count].CopyTo(output[position..]); position += count - 1;
                }
                output[position++] = 'e';
                int power = decimalPosition - 1;
                if (power >= 0) output[position++] = '+';
                power.TryFormat(output[position..], out int powerLength, provider: CultureInfo.InvariantCulture);
                position += powerLength;
            }
            return new string(output[..position]);
        }
    }
}
