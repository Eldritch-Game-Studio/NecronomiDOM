using System;

namespace NecronomiDom.Css.Values.Colors
{
    internal static partial class CssColorConversion
    {
        // Color 4 sample conversions use the same chromaticity-derived white throughout.
        private const double D50X=0.3457/0.3585, D50Z=(1-0.3457-0.3585)/0.3585;
        internal static CssResolvedColor Convert(CssResolvedColor c, string target)
        {
            target=target.ToLowerInvariant();
            c=c with { Space=c.Space.ToLowerInvariant() };
            if (!CssColorParser.IsInterpolationSpace(target) || !CssColorParser.IsInterpolationSpace(c.Space))
                throw new ArgumentException("Unknown CSS color space.");
            if (target == "xyz") target = "xyz-d65";
            if (c.Space == target) return c;
            c=Powerless(c);
            double x = Zero(c.C1), y = Zero(c.C2), z = Zero(c.C3);
            string source = c.Space;
            if (source is "lch" or "oklch")
            { double h = z * System.Math.PI / 180; z = y * System.Math.Sin(h); y *= System.Math.Cos(h); source = source == "lch" ? "lab" : "oklab"; }
            if (source is "hsl" or "hwb")
            { (x, y, z) = HueToRgb(x, y, z, source == "hwb"); source = "srgb"; }
            if (source==target) return new(target,x,y,z,c.Alpha);
            if (source=="srgb" && target is "hsl" or "hwb")
            {
                (x,y,z)=RgbToHue(x,y,z,target=="hwb");
                return Powerless(new(target,x,y,z,c.Alpha));
            }
            if ((source=="lab" && target=="lch") || (source=="oklab" && target=="oklch"))
                return Powerless(new(target,x,System.Math.Sqrt(y*y+z*z),Hue(System.Math.Atan2(z,y)*180/System.Math.PI),c.Alpha));
            if (source == "lab")
            {
                double f = (x + 16) / 116;
                x = LabInverse(f + y / 500) * D50X; z = LabInverse(f - z / 200) * D50Z; y = LabInverse(f);
                source = "xyz-d50";
            }
            if (source == "oklab")
            {
                double l = x + 0.3963377773761749 * y + 0.2158037573099136 * z;
                double m = x - 0.1055613458156586 * y - 0.0638541728258133 * z;
                double s = x - 0.0894841775298119 * y - 1.2914855480194092 * z;
                (x, y, z) = Matrix(l*l*l,m*m*m,s*s*s,1.2268798758459243,-0.5578149944602171,0.2813910456659647,-0.0405757452148008,1.1122868032803170,-0.0717110580655164,-0.0763729366746601,-0.4214933324022432,1.5869240198367816);
                source = "xyz-d65";
            }
            if (source==target) return new(target,x,y,z,c.Alpha);
            if ((source=="srgb" && target=="srgb-linear") || (source=="srgb-linear" && target=="srgb") ||
                (source=="display-p3" && target=="display-p3-linear") || (source=="display-p3-linear" && target=="display-p3"))
            {
                (x,y,z)=target is "srgb" or "display-p3" ? Encode(x,y,z,target) : Decode(x,y,z,source);
                return new(target,x,y,z,c.Alpha);
            }
            if (!source.StartsWith("xyz", StringComparison.Ordinal))
            {
                (x, y, z) = Decode(x, y, z, source);
                (x, y, z) = ToXyz(x, y, z, source);
                if (source == "prophoto-rgb") source = "xyz-d50"; else source = "xyz-d65";
            }
            if (source == "xyz-d50") (x,y,z) = Matrix(x,y,z,0.955473421488075,-0.02309845494876471,0.06325924320057072,-0.0283697093338637,1.0099953980813041,0.021041441191917323,0.012314014864481998,-0.020507649298898964,1.330365926242124);
            if (target is "xyz-d50" or "lab" or "lch" or "prophoto-rgb")
                (x,y,z) = Matrix(x,y,z,1.0479297925449969,0.022946870601609652,-0.05019226628920524,0.02962780877005599,0.9904344267538799,-0.017073799063418826,-0.009243040646204504,0.015055191490298152,0.7518742814281371);
            if (target is "lab" or "lch")
            {
                double fx = LabForward(x / D50X), fy = LabForward(y), fz = LabForward(z / D50Z);
                (x,y,z) = (116*fy-16,500*(fx-fy),200*(fy-fz));
            }
            else if (target is "oklab" or "oklch")
            {
                (x,y,z) = Matrix(x,y,z,0.8190224379967030,0.3619062600528904,-0.1288737815209879,0.0329836539323885,0.9292868615863434,0.0361446663506424,0.0481771893596242,0.2642395317527308,0.6335478284694309);
                (x,y,z) = Matrix(System.Math.Cbrt(x),System.Math.Cbrt(y),System.Math.Cbrt(z),0.2104542683093140,0.7936177747023054,-0.0040720430116193,1.9779985324311684,-2.4285922420485799,0.4505937096174110,0.0259040424655478,0.7827717124575296,-0.8086757549230774);
            }
            else if (!target.StartsWith("xyz", StringComparison.Ordinal))
            {
                string rgbTarget = target is "hsl" or "hwb" ? "srgb" : target;
                (x,y,z) = FromXyz(x,y,z,rgbTarget);
                (x,y,z) = Encode(x,y,z,rgbTarget);
                if (target is "hsl" or "hwb") (x,y,z) = RgbToHue(x,y,z,target == "hwb");
            }
            if (target is "lch" or "oklch") (y,z) = (System.Math.Sqrt(y*y+z*z), Hue(System.Math.Atan2(z,y)*180/System.Math.PI));
            return Powerless(new(target,x,y,z,c.Alpha));
        }
        private static double Zero(double v) => double.IsNaN(v) ? 0 : v;
        internal static double Hue(double h)
        {
            if (double.IsInfinity(h)) return 0;
            double normalized=h%360;
            if (normalized<0) normalized+=360;
            return normalized==360 ? 0 : normalized;
        }
        private static double LabInverse(double t) => t > 6.0/29 ? t*t*t : 3*(6.0/29)*(6.0/29)*(t-4.0/29);
        private static double LabForward(double t) => t > 216.0/24389 ? System.Math.Cbrt(t) : (24389.0/27*t+16)/116;
        private static (double,double,double) Matrix(double x,double y,double z,double a,double b,double c,double d,double e,double f,double g,double h,double i) => (a*x+b*y+c*z,d*x+e*y+f*z,g*x+h*y+i*z);
        private static (double,double,double) HueToRgb(double h,double s,double l,bool hwb)
        {
            h = Hue(h)/60;
            double chroma = hwb ? 1 : (1-System.Math.Abs(2*l-1))*s;
            double secondary = chroma*(1-System.Math.Abs(h%2-1));
            (double r,double g,double b) = h switch { <1 => (chroma,secondary,0d), <2 => (secondary,chroma,0d), <3 => (0d,chroma,secondary), <4 => (0d,secondary,chroma), <5 => (secondary,0d,chroma), _ => (chroma,0d,secondary) };
            if (hwb)
            {
                if (s+l >= 1) return (s/(s+l),s/(s+l),s/(s+l));
                return (r*(1-s-l)+s,g*(1-s-l)+s,b*(1-s-l)+s);
            }
            double m = l-chroma/2; return (r+m,g+m,b+m);
        }
        private static (double,double,double) RgbToHue(double r,double g,double b,bool hwb)
        {
            double min = System.Math.Min(r,System.Math.Min(g,b)), max = System.Math.Max(r,System.Math.Max(g,b)), delta = max-min;
            double l=(min+max)/2;
            // A few rounding ULPs of chroma are not meaningful at an achromatic endpoint.
            // Avoid dividing by a nearly-zero lightness denominator without clipping a gamut.
            double roundoff=8*2.2204460492503131e-16*System.Math.Max(System.Math.Abs(min),System.Math.Abs(max));
            if (delta<=roundoff) delta=0;
            double h = delta == 0 ? double.NaN : max == r ? (g-b)/delta : max == g ? (b-r)/delta+2 : (r-g)/delta+4;
            if (hwb) return (Hue(h*60),min,1-max);
            double saturation=delta==0 || l is 0 or 1 ? 0 : (max-l)/System.Math.Min(l,1-l);
            if (saturation<0) { h+=3; saturation=-saturation; }
            return (Hue(h*60),saturation,l);
        }
    }
}
