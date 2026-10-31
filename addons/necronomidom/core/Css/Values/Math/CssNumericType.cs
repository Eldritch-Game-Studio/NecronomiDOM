using System;

namespace NecronomiDom.Css.Values.Math
{
    /// <summary>CSS Values 4 § 10.9 / Typed OM § 4.4 numeric base dimensions.</summary>
    public enum CssNumericDimension { Number = -1, Length, Angle, Time, Frequency, Resolution, Flex, Percentage }

    /// <summary>CSS Typed OM numeric exponent map and percentage hint, used by Values 4 math type checking.</summary>
    public readonly record struct CssNumericType(int Length = 0, int Angle = 0, int Time = 0,
        int Frequency = 0, int Resolution = 0, int Flex = 0, int Percentage = 0,
        CssNumericDimension? PercentageHint = null)
    {
        public bool IsNumber => SamePowers(default) && PercentageHint == null;
        public bool IsPercentage => SamePowers(For(CssNumericDimension.Percentage)) &&
            PercentageHint is null or CssNumericDimension.Percentage;
        public bool IsDimension(CssNumericDimension dimension) => SamePowers(For(dimension)) && PercentageHint == null;
        public bool Matches(CssNumericDimension dimension, bool allowPercentage) =>
            SamePowers(For(dimension)) && (PercentageHint == null || allowPercentage && PercentageHint == dimension)
            || allowPercentage && IsPercentage;
        public bool MatchesNumber(CssNumericDimension? percentageContext) => SamePowers(default) &&
            (PercentageHint == null || percentageContext.HasValue && (PercentageHint == percentageContext ||
                percentageContext == CssNumericDimension.Number && PercentageHint == CssNumericDimension.Percentage));
        public int Exponent(CssNumericDimension dimension) => dimension switch
        {
            CssNumericDimension.Length => Length, CssNumericDimension.Angle => Angle,
            CssNumericDimension.Time => Time, CssNumericDimension.Frequency => Frequency,
            CssNumericDimension.Resolution => Resolution, CssNumericDimension.Flex => Flex,
            CssNumericDimension.Percentage => Percentage, _ => 0
        };
        internal bool SamePowers(CssNumericType other) => Length == other.Length && Angle == other.Angle &&
            Time == other.Time && Frequency == other.Frequency && Resolution == other.Resolution &&
            Flex == other.Flex && Percentage == other.Percentage;
        public static CssNumericType For(CssNumericDimension dimension) => dimension switch
        {
            CssNumericDimension.Length => new(Length: 1), CssNumericDimension.Angle => new(Angle: 1),
            CssNumericDimension.Time => new(Time: 1), CssNumericDimension.Frequency => new(Frequency: 1),
            CssNumericDimension.Resolution => new(Resolution: 1), CssNumericDimension.Flex => new(Flex: 1),
            CssNumericDimension.Percentage => new(Percentage: 1), _ => default
        };
        internal CssNumericType Hint(CssNumericDimension hint)
        {
            CssNumericType result = this with { Percentage = 0, PercentageHint = hint };
            return hint switch
            {
                CssNumericDimension.Length => result with { Length = Length + Percentage },
                CssNumericDimension.Angle => result with { Angle = Angle + Percentage },
                CssNumericDimension.Time => result with { Time = Time + Percentage },
                CssNumericDimension.Frequency => result with { Frequency = Frequency + Percentage },
                CssNumericDimension.Resolution => result with { Resolution = Resolution + Percentage },
                CssNumericDimension.Flex => result with { Flex = Flex + Percentage },
                CssNumericDimension.Percentage => result with { Percentage = Percentage }, _ => result
            };
        }
        internal static bool Add(CssNumericType a, CssNumericType b, out CssNumericType result)
        {
            result = default;
            if (a.PercentageHint != null && b.PercentageHint != null && a.PercentageHint != b.PercentageHint) return false;
            CssNumericDimension? hint = a.PercentageHint ?? b.PercentageHint;
            if (hint.HasValue) { a = a.Hint(hint.Value); b = b.Hint(hint.Value); }
            if (a.SamePowers(b)) { result = a; return true; }
            if (hint.HasValue) return false;
            for (CssNumericDimension d = CssNumericDimension.Length; d < CssNumericDimension.Percentage; d++)
            {
                CssNumericType ah = a.Hint(d), bh = b.Hint(d);
                if (ah.SamePowers(bh)) { result = ah; return true; }
            }
            return false;
        }
        internal static bool Multiply(CssNumericType a, CssNumericType b, bool inverse, out CssNumericType result)
        {
            result = default;
            if (a.PercentageHint != null && b.PercentageHint != null && a.PercentageHint != b.PercentageHint) return false;
            CssNumericDimension? hint = a.PercentageHint ?? b.PercentageHint;
            if (hint.HasValue) { a = a.Hint(hint.Value); b = b.Hint(hint.Value); }
            int sign = inverse ? -1 : 1;
            result = new(a.Length + sign * b.Length, a.Angle + sign * b.Angle, a.Time + sign * b.Time,
                a.Frequency + sign * b.Frequency, a.Resolution + sign * b.Resolution, a.Flex + sign * b.Flex,
                a.Percentage + sign * b.Percentage, hint);
            return true;
        }
        internal bool IsSimple
        {
            get
            {
                if (SamePowers(default)) return true;
                for (CssNumericDimension d = CssNumericDimension.Length; d <= CssNumericDimension.Percentage; d++)
                    if (SamePowers(For(d))) return true;
                return false;
            }
        }
        internal string CanonicalUnit
        {
            get
            {
                for (CssNumericDimension d = CssNumericDimension.Length; d <= CssNumericDimension.Percentage; d++)
                    if (SamePowers(For(d))) return CssMathUnits.Canonical(d);
                return "";
            }
        }
    }
}
