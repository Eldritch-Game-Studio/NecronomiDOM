namespace NecronomiDom.Css.Values.Colors
{
    internal static partial class CssColorConversion
    {
        internal static CssResolvedColor Powerless(CssResolvedColor color)
        {
            if (color.Space=="hsl" && ((color.C2>=0 && color.C2<=0.00001) || color.C3 is 0 or 1))
                return color with { C1=double.NaN, C2=0 };
            if (color.Space=="hwb")
            {
                double white=Zero(color.C2), black=Zero(color.C3), sum=white+black;
                // The inclusive sum threshold must tolerate its final addition's rounding ULP.
                if (sum>=1-0.00001-2.2204460492503131e-16)
                {
                    // Color 4 § 4.4.1: remove residual colorfulness, retaining missing components.
                    if (sum<1)
                    {
                        if (double.IsNaN(color.C3)) color=color with { C2=1 };
                        else if (double.IsNaN(color.C2)) color=color with { C3=1 };
                        else color=color with { C3=1-color.C2 };
                    }
                    return color with { C1=double.NaN };
                }
            }
            double epsilon=color.Space=="lch" ? 0.0015 : color.Space=="oklch" ? 0.000004 : -1;
            if (epsilon>=0 && color.C2>=0 && color.C2<=epsilon)
                return color with { C2=0, C3=double.NaN };
            return color;
        }
    }
}
