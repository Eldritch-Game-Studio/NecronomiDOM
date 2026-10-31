using System;

namespace NecronomiDom.Css.Values
{
    internal static class CssNumericRange
    {
        // Chrome155 native specified values support finite single-precision numeric literals.
        internal const double LiteralLimit = float.MaxValue;
        internal static double Literal(double number) => System.Math.Clamp(number, -LiteralLimit, LiteralLimit);
        internal static double IntegerLiteral(double number) => System.Math.Clamp(number, int.MinValue, int.MaxValue);
        internal static double Canonical(double number, string unit) => NecronomiDom.Dom.Names.AsciiLower(unit) switch
        {
            "rad" => number * 180 / System.Math.PI,
            "grad" => number * 0.9,
            "turn" => number * 360,
            "ms" => number / 1000,
            "khz" => number * 1000,
            "dpi" => number / 96,
            "dpcm" => number * 2.54 / 96,
            "cm" => number * 96 / 2.54,
            "mm" => number * 96 / 25.4,
            "q" => number * 96 / 101.6,
            "in" => number * 96,
            "pt" => number * 4 / 3,
            "pc" => number * 16,
            _ => number,
        };
    }
}
