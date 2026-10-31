namespace NecronomiDom.Css.Values.Math
{
    /// <summary>CSS Typed OM § 5.1 numeric matching uses the consuming production's percentage context.</summary>
    public partial class CssMathValue
    {
        public bool Fits(string type, CssNumericDimension? percentageContext)
        {
            if (type is "number" or "integer") return Type.MatchesNumber(percentageContext);
            CssNumericDimension? dimension = type switch
            {
                "length" => CssNumericDimension.Length, "angle" => CssNumericDimension.Angle,
                "time" => CssNumericDimension.Time, "frequency" => CssNumericDimension.Frequency,
                "resolution" => CssNumericDimension.Resolution, "flex" => CssNumericDimension.Flex, _ => null
            };
            if (!dimension.HasValue) return Fits(type);
            return Type.SamePowers(CssNumericType.For(dimension.Value)) && (Type.PercentageHint == null ||
                percentageContext is not null and not CssNumericDimension.Number and not CssNumericDimension.Percentage &&
                Type.PercentageHint == percentageContext);
        }
    }
}
