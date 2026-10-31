using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Colors
{
    internal static class CssColorChannels
    {
        private static readonly string[] RgbNames={"r","g","b","alpha"}, HslNames={"h","s","l","alpha"}, HwbNames={"h","w","b","alpha"},
            LabNames={"l","a","b","alpha"}, LchNames={"l","c","h","alpha"}, XyzNames={"x","y","z","alpha"};
        internal static IReadOnlyList<string> Names(string space) => space switch
        {
            "hsl"=>HslNames, "hwb"=>HwbNames, "lab" or "oklab"=>LabNames, "lch" or "oklch"=>LchNames,
            "xyz" or "xyz-d50" or "xyz-d65"=>XyzNames, _=>RgbNames
        };
        internal static bool TryRead(ComponentValue component, string space, int index, IReadOnlyDictionary<string,double>? symbols, out double value, CssMathEvaluationContext? numeric = null, bool validateOnly = false)
        {
            value = 0;
            bool angle = index < 3 && (space is "hsl" or "hwb" ? index == 0 : space is "lch" or "oklch" && index == 2);
            if (component is PreservedToken p)
            {
                CssToken t = p.Token;
                if (t.IsIdent("none")) { value = double.NaN; return true; }
                if (t.Kind == CssTokenKind.Ident && symbols != null)
                {
                    if (!symbols.TryGetValue(t.Value,out double symbol)) return false;
                    value = symbol*NumberScale(space,index); return true;
                }
                if (t.Kind == CssTokenKind.Number) { value = t.Number * NumberScale(space,index); return true; }
                if (t.Kind == CssTokenKind.Percentage && !angle) { value = t.Number * PercentScale(space,index); return true; }
                if (t.Kind == CssTokenKind.Dimension && angle)
                {
                    double factor = t.Unit.ToLowerInvariant() switch { "deg" => 1, "grad" => 0.9, "rad" => 180/System.Math.PI, "turn" => 360, _ => double.NaN };
                    value = t.Number*factor; return !double.IsNaN(factor);
                }
                return false;
            }
            if (component is not FunctionValue function) return false;
            CssValue? parsed;
            bool ok=ParseMath(function,space,symbols!=null,out parsed);
            return ok && parsed is CssMathValue math && TryMath(math,space,index,symbols,numeric,validateOnly,out value);
        }
        internal static bool ParseMath(FunctionValue function,string space,bool relative,out CssValue? value) =>
            relative ? CssMathParser.TryParseChannels(function,Names(space),out value) : CssMathParser.TryParse(function,out value);
        internal static bool TryMath(CssMathValue math,string space,int index,IReadOnlyDictionary<string,double>? symbols,CssMathEvaluationContext? numeric,bool validateOnly,out double value)
        {
            value=0;
            bool angle=index<3 && (space is "hsl" or "hwb" ? index==0 : space is "lch" or "oklch" && index==2);
            if (validateOnly) return math.Fits("number") || (!angle && math.Fits("percentage")) || (angle && math.Fits("angle"));
            double? Resolve(CssValue external)
            {
                if (external is CssKeywordValue keyword && symbols!=null && symbols.TryGetValue(keyword.Value,out double channel)) return channel;
                return numeric?.ResolveNumericValue?.Invoke(external);
            }
            bool percentage=math.Fits("percentage");
            var context=new CssMathEvaluationContext(numeric?.ResolveUnit,100,math.Type.PercentageHint,numeric?.DevicePixelRatio,Resolve);
            if (!math.TryEvaluate(context,out double constant)) return false;
            if (double.IsNaN(constant)) constant=0;
            if (!angle && percentage) { value=constant*PercentScale(space,index); return true; }
            if (math.Fits("number")) { value=constant*NumberScale(space,index); return true; }
            if (angle && math.Fits("angle")) { value=constant; return true; }
            return false;
        }
        private static double NumberScale(string space,int index) => index == 3 ? 1 : space == "rgb" ? 1.0/255 : space is "hsl" or "hwb" && index > 0 ? 0.01 : 1;
        private static double PercentScale(string space,int index) => index == 3 ? 0.01 : space switch
        {
            "lab" => index == 0 ? 1 : 1.25,
            "lch" => index == 0 ? 1 : 1.5,
            "oklab" => index == 0 ? 0.01 : 0.004,
            "oklch" => index == 0 ? 0.01 : 0.004,
            _ => 0.01
        };
        internal static double SpecifiedPercentage(string space,int index,double percentage) => percentage*PercentScale(space,index)/NumberScale(space,index);
        internal static CssResolvedColor Clamp(string space,double c1,double c2,double c3,double alpha)
        {
            if (!double.IsNaN(alpha)) alpha = System.Math.Clamp(alpha,0,1);
            if (space == "rgb")
            {
                return new("srgb",ClampUnit(c1),ClampUnit(c2),ClampUnit(c3),alpha);
            }
            if (space=="hsl") return new(space,CssColorConversion.Hue(c1),System.Math.Max(c2,0),c3,alpha);
            if (space=="hwb") return new(space,CssColorConversion.Hue(c1),c2,c3,alpha);
            if (space is "lab" or "lch") c1 = System.Math.Clamp(c1,0,100);
            if (space is "oklab" or "oklch") c1 = System.Math.Clamp(c1,0,1);
            if (space is "lch" or "oklch") { c2 = System.Math.Max(0,c2); c3 = CssColorConversion.Hue(c3); }
            return new(space,c1,c2,c3,alpha);
        }
        private static double ClampUnit(double v) => System.Math.Clamp(v,0,1);
        internal static Dictionary<string,double> Symbols(CssResolvedColor origin,string space)
        {
            var symbols = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase) { ["alpha"] = origin.Alpha };
            IReadOnlyList<string> channels=Names(space);
            double scale = space == "rgb" ? 255 : 1;
            symbols[channels[0]] = origin.C1*scale;
            symbols[channels[1]] = origin.C2*(space is "hsl" or "hwb" ? 100 : scale);
            symbols[channels[2]] = origin.C3*(space is "hsl" or "hwb" ? 100 : scale);
            return symbols;
        }
    }
}
