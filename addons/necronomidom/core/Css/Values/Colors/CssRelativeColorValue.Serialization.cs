using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Colors
{
    public sealed partial class CssRelativeColorValue
    {
        private bool TryChannel(int index,IReadOnlyDictionary<string,double>? symbols,CssColorContext context,out double value)
        {
            if (serializedChannels[index] is CssMathValue math)
                return CssColorChannels.TryMath(math,Space,index,symbols,context.NumericContext,false,out value);
            return CssColorChannels.TryRead(Channels[index],Space,index,symbols,out value,context.NumericContext);
        }
        private IReadOnlyList<CssValue> SerializeChannels()
        {
            var result=new CssValue[Channels.Count];
            for (int i=0;i<Channels.Count;i++)
            {
                ComponentValue channel=Channels[i];
                if (channel is FunctionValue function)
                {
                    CssValue? math;
                    bool valid=CssColorChannels.ParseMath(function,Space,Origin!=null,out math);
                    if (!valid || math==null) throw new InvalidOperationException("Validated color channel has no math serialization.");
                    result[i]=math;
                }
                else if (Origin!=null && channel is PreservedToken relativePercentage && relativePercentage.Token.Kind==CssTokenKind.Percentage)
                    result[i]=new CssPercentageValue(relativePercentage.Token.Number);
                else if (channel is PreservedToken p && p.Token.Kind==CssTokenKind.Percentage)
                {
                    double number=CssColorChannels.SpecifiedPercentage(Space,i,p.Token.Number);
                    result[i]=new CssNumberValue(NormalizeNumber(number,i),false);
                }
                else if (Origin==null && channel is PreservedToken angle && angle.Token.Kind==CssTokenKind.Dimension && CssColorChannels.TryRead(channel,Space,i,null,out double degrees))
                    result[i]=new CssNumberValue(NormalizeNumber(degrees,i),false);
                else
                {
                    CssValue? parsed=CssValueParser.Parse(new[] { channel });
                    if (parsed==null) throw new InvalidOperationException("Validated color channel has no serialization.");
                    result[i]=NormalizeLiteral(parsed,i);
                }
            }
            return result;
        }
        private CssValue NormalizeLiteral(CssValue value,int index)
        {
            if (value is not CssNumberValue number) return value;
            double normalized=NormalizeNumber(number.Value,index);
            return normalized==number.Value ? value : new CssNumberValue(normalized,false);
        }
        private double NormalizeNumber(double n,int index)
        {
            if (Origin==null)
            {
                if (index==3) n=System.Math.Clamp(n,0,1);
                if (Space=="rgb" && index<3) n=System.Math.Clamp(n,0,255);
                if (Space=="hsl" && index==1) n=System.Math.Max(n,0);
                if (Space is "lab" or "lch" && index==0) n=System.Math.Clamp(n,0,100);
                if (Space is "oklab" or "oklch" && index==0) n=System.Math.Clamp(n,0,1);
                if (Space is "lch" or "oklch" && index==1) n=System.Math.Max(n,0);
                if (index<3 && (Space is "hsl" or "hwb" ? index==0 : Space is "lch" or "oklch" && index==2)) n=CssColorConversion.Hue(n);
            }
            return n;
        }
    }
}
