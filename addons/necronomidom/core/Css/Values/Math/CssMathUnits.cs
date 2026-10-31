using System;
using NecronomiDom.Dom;
using M = System.Math;

namespace NecronomiDom.Css.Values.Math
{
    /// <summary>CSS Values 4 §§ 6–9 unit classification and exact absolute-unit ratios.</summary>
    public static class CssMathUnits
    {
        public static bool TryGet(string unit, out CssNumericDimension dimension, out double? canonicalScale)
        {
            unit = Names.AsciiLower(unit);
            canonicalScale = null;
            dimension = CssNumericDimension.Length;
            switch (unit)
            {
                case "": dimension = CssNumericDimension.Number; canonicalScale = 1; return true;
                case "%": dimension = CssNumericDimension.Percentage; return true;
                case "px": canonicalScale = 1; return true;
                case "in": canonicalScale = 96; return true;
                case "cm": canonicalScale = 96 / 2.54; return true;
                case "mm": canonicalScale = 96 / 25.4; return true;
                case "q": canonicalScale = 96 / 101.6; return true;
                case "pt": canonicalScale = 96 / 72.0; return true;
                case "pc": canonicalScale = 16; return true;
                case "em": case "ex": case "cap": case "ch": case "ic": case "lh":
                case "rem": case "rex": case "rcap": case "rch": case "ric": case "rlh":
                case "vw": case "vh": case "vi": case "vb": case "vmin": case "vmax":
                case "svw": case "svh": case "svi": case "svb": case "svmin": case "svmax":
                case "lvw": case "lvh": case "lvi": case "lvb": case "lvmin": case "lvmax":
                case "dvw": case "dvh": case "dvi": case "dvb": case "dvmin": case "dvmax":
                case "cqw": case "cqh": case "cqi": case "cqb": case "cqmin": case "cqmax": return true;
                case "deg": dimension = CssNumericDimension.Angle; canonicalScale = 1; return true;
                case "grad": dimension = CssNumericDimension.Angle; canonicalScale = 0.9; return true;
                case "rad": dimension = CssNumericDimension.Angle; canonicalScale = 180 / M.PI; return true;
                case "turn": dimension = CssNumericDimension.Angle; canonicalScale = 360; return true;
                case "s": dimension = CssNumericDimension.Time; canonicalScale = 1; return true;
                case "ms": dimension = CssNumericDimension.Time; canonicalScale = 0.001; return true;
                case "hz": dimension = CssNumericDimension.Frequency; canonicalScale = 1; return true;
                case "khz": dimension = CssNumericDimension.Frequency; canonicalScale = 1000; return true;
                case "dppx": case "x": dimension = CssNumericDimension.Resolution; canonicalScale = 1; return true;
                case "dpi": dimension = CssNumericDimension.Resolution; canonicalScale = 1 / 96.0; return true;
                case "dpcm": dimension = CssNumericDimension.Resolution; canonicalScale = 2.54 / 96; return true;
                case "fr": dimension = CssNumericDimension.Flex; canonicalScale = 1; return true;
                default: return false;
            }
        }
        public static string Canonical(CssNumericDimension dimension) => dimension switch
        {
            CssNumericDimension.Length => "px", CssNumericDimension.Angle => "deg",
            CssNumericDimension.Time => "s", CssNumericDimension.Frequency => "hz",
            CssNumericDimension.Resolution => "dppx", CssNumericDimension.Flex => "fr",
            CssNumericDimension.Percentage => "%", _ => ""
        };
    }

    /// <summary>Values 4 § 10.11 computation inputs; resolver returns the canonical magnitude of one relative unit.</summary>
    public sealed class CssMathEvaluationContext
    {
        internal static readonly CssMathEvaluationContext Specified = new();
        internal static readonly CssMathEvaluationContext NumberPercentage = new(percentageBasis: 1,
            percentageHint: CssNumericDimension.Number);
        // Identity conversion in percentage units, for a consuming number-percentage grammar's positive percentage scale.
        internal static readonly CssMathEvaluationContext PercentageMagnitudes = new(percentageBasis: 100,
            percentageHint: CssNumericDimension.Percentage);
        public CssMathEvaluationContext(Func<string, double?>? resolveUnit = null, double? percentageBasis = null,
            CssNumericDimension? percentageHint = null, double? devicePixelRatio = null,
            Func<CssValue, double?>? resolveNumericValue = null, double? intrinsicSize = null)
        {
            ResolveUnit = resolveUnit; PercentageBasis = percentageBasis; PercentageHint = percentageHint;
            DevicePixelRatio = devicePixelRatio;
            ResolveNumericValue = resolveNumericValue;
            IntrinsicSize = intrinsicSize;
        }
        public Func<string, double?>? ResolveUnit { get; }
        public double? PercentageBasis { get; }
        public CssNumericDimension? PercentageHint { get; }
        public double? DevicePixelRatio { get; }
        public Func<CssValue, double?>? ResolveNumericValue { get; }
        public double? IntrinsicSize { get; }
        internal bool TryEvaluatePercentage(double percentage, out double value)
        {
            value = percentage;
            double? basis = PercentageBasis ?? (PercentageHint == CssNumericDimension.Number ? 1.0 : null);
            if (!basis.HasValue) return false;
            // Preserve percentage magnitudes in percentage units; numeric contexts apply the ratio before the basis.
            value = PercentageHint == CssNumericDimension.Percentage
                ? percentage * (basis.Value / 100) : (percentage / 100) * basis.Value;
            return true;
        }
        internal bool TryScale(string unit, out double scale)
        {
            scale = 1;
            if (unit == "%")
            {
                double? basis = PercentageBasis ?? (PercentageHint == CssNumericDimension.Number ? 1.0 : null);
                if (!basis.HasValue) return false;
                scale = basis.Value / 100; return true;
            }
            if (!CssMathUnits.TryGet(unit, out _, out double? absolute)) return false;
            double? resolved = absolute ?? ResolveUnit?.Invoke(unit);
            if (!resolved.HasValue) return false;
            scale = resolved.Value; return true;
        }
    }
}
