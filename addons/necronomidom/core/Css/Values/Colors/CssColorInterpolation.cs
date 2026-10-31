using System;

namespace NecronomiDom.Css.Values.Colors
{
    internal static class CssColorInterpolation
    {
        internal static CssResolvedColor ConvertMissing(CssResolvedColor color,string target)
        {
            int missing=Analogous(color,target);
            CssResolvedColor result=color.ConvertTo(target);
            return result with { C1=(missing&1)!=0 ? double.NaN : result.C1, C2=(missing&2)!=0 ? double.NaN : result.C2, C3=(missing&4)!=0 ? double.NaN : result.C3 };
        }
        internal static CssResolvedColor Interpolate(CssResolvedColor a,CssResolvedColor b,string space,string hueMethod,double progress)
        {
            int missingA = Analogous(a,space), missingB = Analogous(b,space);
            a=a.ConvertTo(space); b=b.ConvertTo(space);
            double alpha1 = (missingA&8)!=0 ? double.NaN : a.Alpha, alpha2=(missingB&8)!=0 ? double.NaN : b.Alpha;
            Carry(ref alpha1,ref alpha2);
            bool missingAlpha=double.IsNaN(alpha1);
            if (missingAlpha) { alpha1=1; alpha2=1; }
            double alpha=alpha1*(1-progress)+alpha2*progress;
            int hueIndex=space is "hsl" or "hwb" ? 0 : space is "lch" or "oklch" ? 2 : -1;
            double x1=a.C1,y1=a.C2,z1=a.C3,x2=b.C1,y2=b.C2,z2=b.C3;
            if ((missingA&1)!=0) x1=double.NaN; if ((missingA&2)!=0) y1=double.NaN; if ((missingA&4)!=0) z1=double.NaN;
            if ((missingB&1)!=0) x2=double.NaN; if ((missingB&2)!=0) y2=double.NaN; if ((missingB&4)!=0) z2=double.NaN;
            Carry(ref x1,ref x2); Carry(ref y1,ref y2); Carry(ref z1,ref z2);
            return new(space,Blend(x1,x2,progress,alpha1,alpha2,alpha,hueIndex==0,hueMethod),Blend(y1,y2,progress,alpha1,alpha2,alpha,false,hueMethod),Blend(z1,z2,progress,alpha1,alpha2,alpha,hueIndex==2,hueMethod),missingAlpha ? double.NaN : alpha);
        }
        private static int Analogous(CssResolvedColor color,string target)
        {
            if (color.Space==target) return color.Missing;
            int result=color.Missing&8, remainingSource=7, remainingTarget=7;
            for (int i=0;i<3;i++)
                for (int j=0;j<3;j++)
                    if (Category(color.Space,i)==Category(target,j))
                    {
                        remainingSource&=~(1<<i); remainingTarget&=~(1<<j);
                        if ((color.Missing&(1<<i))!=0) result|=1<<j;
                    }
            if (remainingSource!=0 && (color.Missing&remainingSource)==remainingSource) result|=remainingTarget;
            return result;
        }
        private static char Category(string space,int index) => space switch
        {
            "lab" or "oklab" => index==0 ? 'L' : index==1 ? 'a' : 'b',
            "lch" or "oklch" => index==0 ? 'L' : index==1 ? 'C' : 'H',
            "hsl" => index==0 ? 'H' : index==1 ? 'C' : 'L',
            "hwb" => index==0 ? 'H' : index==1 ? 'W' : 'K',
            _ => index==0 ? 'R' : index==1 ? 'G' : 'B'
        };
        private static void Carry(ref double a,ref double b)
        { if (double.IsNaN(a)) a=b; if (double.IsNaN(b)) b=a; }
        private static double Blend(double a,double b,double progress,double alpha1,double alpha2,double alpha,bool hue,string method)
        {
            if (double.IsNaN(a) && double.IsNaN(b)) return double.NaN;
            if (!hue) return alpha==0 ? 0 : (a*alpha1*(1-progress)+b*alpha2*progress)/alpha;
            a=CssColorConversion.Hue(a); b=CssColorConversion.Hue(b); double delta=b-a;
            if (method=="shorter") { if (delta>180) a+=360; else if (delta < -180) b+=360; }
            else if (method=="longer") { if (delta is >0 and <180) a+=360; else if (delta is >-180 and <=0) b+=360; }
            else if (method=="increasing" && b<a) b+=360;
            else if (method=="decreasing" && a<b) a+=360;
            return CssColorConversion.Hue(a*(1-progress)+b*progress);
        }
    }
}
