namespace NecronomiDom.Css.Values
{
    /// <summary>CSS Fonts 4 font-width keyword percentages; CSS font shorthand retains the CSS3 keyword grammar.</summary>
    internal static class CssFontWidthValues
    {
        internal static double Percentage(string keyword) => keyword switch
        {
            "ultra-condensed" => 50, "extra-condensed" => 62.5, "condensed" => 75,
            "semi-condensed" => 87.5, "normal" => 100, "semi-expanded" => 112.5,
            "expanded" => 125, "extra-expanded" => 150, "ultra-expanded" => 200, _ => double.NaN
        };
        internal static string? Keyword(double percentage) => percentage switch
        {
            50 => "ultra-condensed", 62.5 => "extra-condensed", 75 => "condensed",
            87.5 => "semi-condensed", 100 => "normal", 112.5 => "semi-expanded",
            125 => "expanded", 150 => "extra-expanded", 200 => "ultra-expanded", _ => null
        };
        private static double Percentage(CssValue value) => value switch
        {
            CssPercentageValue percentage => percentage.Value,
            CssKeywordValue keyword => Percentage(keyword.Value), _ => double.NaN
        };
        internal static bool Equivalent(CssValue left, CssValue right)
        {
            double percentage = Percentage(left);
            return !double.IsNaN(percentage) && percentage == Percentage(right);
        }
    }
}
