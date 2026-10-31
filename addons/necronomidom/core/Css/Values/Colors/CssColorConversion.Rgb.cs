using System;

namespace NecronomiDom.Css.Values.Colors
{
    internal static partial class CssColorConversion
    {
        private static (double,double,double) Decode(double x,double y,double z,string space) => (Transfer(x,space,false),Transfer(y,space,false),Transfer(z,space,false));
        private static (double,double,double) Encode(double x,double y,double z,string space) => (Transfer(x,space,true),Transfer(y,space,true),Transfer(z,space,true));
        private static double Transfer(double value,string space,bool encode)
        {
            if (double.IsNaN(value)) return 0;
            double v = System.Math.Abs(value), sign = System.Math.Sign(value);
            double result = space switch
            {
                "srgb-linear" or "display-p3-linear" => v,
                "a98-rgb" => System.Math.Pow(v,encode ? 256.0/563 : 563.0/256),
                "prophoto-rgb" => encode ? (v <= 1.0/512 ? v*16 : System.Math.Pow(v,1.0/1.8)) : (v <= 1.0/32 ? v/16 : System.Math.Pow(v,1.8)),
                // Color 4 uses the display EOTF (BT.1886), not the BT.2020 camera OETF.
                "rec2020" => System.Math.Pow(v,encode ? 1/2.4 : 2.4),
                _ => encode ? (v <= 0.0031308 ? 12.92*v : 1.055*System.Math.Pow(v,1/2.4)-0.055) : (v <= 0.04045 ? v/12.92 : System.Math.Pow((v+0.055)/1.055,2.4))
            };
            return sign*result;
        }
        private static (double,double,double) ToXyz(double x,double y,double z,string space) => space switch
        {
            "display-p3" or "display-p3-linear" => Matrix(x,y,z,608311d/1250200,189793d/714400,198249d/1000160,35783d/156275,247089d/357200,198249d/2500400,0,32229d/714400,5220557d/5000800),
            "a98-rgb" => Matrix(x,y,z,573536d/994567,263643d/1420810,187206d/994567,591459d/1989134,6239551d/9945670,374412d/4972835,53769d/1989134,351524d/4972835,4929758d/4972835),
            "prophoto-rgb" => Matrix(x,y,z,0.79776664490064230,0.13518129740053308,0.03134773412839220,0.28807482881940130,0.71183523424187300,0.00008993693872564,0,0,0.82510460251046020),
            "rec2020" => Matrix(x,y,z,63426534d/99577255,20160776d/139408157,47086771d/278816314,26158966d/99577255,472592308d/697040785,8267143d/139408157,0,19567812d/697040785,295819943d/278816314),
            _ => Matrix(x,y,z,506752d/1228815,87881d/245763,12673d/70218,87098d/409605,175762d/245763,12673d/175545,7918d/409605,87881d/737289,1001167d/1053270)
        };
        private static (double,double,double) FromXyz(double x,double y,double z,string space) => space switch
        {
            "display-p3" or "display-p3-linear" => Matrix(x,y,z,446124d/178915,-333277d/357830,-72051d/178915,-14852d/17905,63121d/35810,423d/17905,11844d/330415,-50337d/660830,316169d/330415),
            "a98-rgb" => Matrix(x,y,z,1829569d/896150,-506331d/896150,-308931d/896150,-851781d/878810,1648619d/878810,36519d/878810,16779d/1248040,-147721d/1248040,1266979d/1248040),
            "prophoto-rgb" => Matrix(x,y,z,1.34578688164715830,-0.25557208737979464,-0.05110186497554526,-0.54463070512490190,1.50824774284514680,0.02052744743642139,0,0,1.21196754563894520),
            "rec2020" => Matrix(x,y,z,30757411d/17917100,-6372589d/17917100,-4539589d/17917100,-19765991d/29648200,47925759d/29648200,467509d/29648200,792561d/44930125,-1921689d/44930125,42328811d/44930125),
            _ => Matrix(x,y,z,12831d/3959,-329d/214,-1974d/3959,-851781d/878810,1648619d/878810,36519d/878810,705d/12673,-2585d/12673,705d/667)
        };
    }
}
