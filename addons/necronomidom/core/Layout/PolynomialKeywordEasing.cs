namespace NecronomiDom.Layout;

/// <summary>Polynomial approximations of the CSS keyword easings used by the simplified transition profiles;
/// other timing functions fall back to the CSS cubic-bezier. Never a replacement CSS resolver.</summary>
internal static class PolynomialKeywordEasing
{
    internal static double Sample(string? keyword, CssCubicEasing css, double progress)
    {
        float t = (float)progress;
        return keyword switch
        {
            "ease" => t * (1.8f + t * (-.6f + t * -.2f)),
            "ease-in" => t * t,
            "ease-out" => t * (2 - t),
            "ease-in-out" => InOutQuad(t),
            "linear" => t,
            _ => css.Sample(progress)
        };
    }
    private static float InOutQuad(float t)
    {
        t *= 2;
        return t < 1 ? t * t * .5f : -.5f * ((t - 1) * (t - 3) - 1);
    }
}
