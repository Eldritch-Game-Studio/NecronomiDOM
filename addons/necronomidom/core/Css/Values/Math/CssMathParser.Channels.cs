using NecronomiDom.Dom;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Math
{
    public static partial class CssMathParser
    {
        /// <summary>Color 5 § 4.1 channel math: retain registered identifiers while simplifying numeric subtrees.</summary>
        public static bool TryParseChannels(FunctionValue function, IReadOnlyCollection<string> channelNames, out CssValue? value)
            => TryParseChannels(function, channelNames, (CssNumericDimension?)null, out value);
        public static bool TryParseChannels(FunctionValue function, IReadOnlyCollection<string> channelNames,
            CssNumericDimension? percentageHint, out CssValue? value)
        {
            return TryParse(new FunctionValue(function.NameToken, Symbolize(function.Arguments, channelNames)), percentageHint, out value);
        }
        public static bool TryParseChannels(FunctionValue function, IReadOnlyCollection<string> channelNames,
            CssMathEvaluationContext specifiedContext, out CssValue? value)
        {
            return TryParse(new FunctionValue(function.NameToken, Symbolize(function.Arguments, channelNames)), specifiedContext, out value);
        }
        public static bool TryParseChannels(FunctionValue function, IReadOnlyCollection<string> channelNames,
            CssNumericDimension? percentageHint, double percentageBasis, out CssValue? value) =>
            TryParseChannels(function, channelNames,
                new CssMathEvaluationContext(percentageBasis: percentageBasis, percentageHint: percentageHint), out value);
        private static IReadOnlyList<ComponentValue> Symbolize(IReadOnlyList<ComponentValue> values, IReadOnlyCollection<string> names,
            CssNumericType type = default)
        {
            var result = new ComponentValue[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                ComponentValue value = values[i];
                if (value is PreservedToken p && p.Token.Kind == CssTokenKind.Ident && NamedChannel(names, p.Token.Value))
                    value = new ChannelComponent(p.Token, type);
                else if (value is FunctionValue function)
                    value = new FunctionValue(function.NameToken, Symbolize(function.Arguments, names, type));
                else if (value is SimpleBlock block)
                    value = new SimpleBlock(block.OpenerToken, Symbolize(block.Value, names, type));
                result[i] = value;
            }
            return result;
        }
        private static bool NamedChannel(IReadOnlyCollection<string> names, string identifier)
        {
            if (names is IReadOnlyList<string> list)
            {
                for (int i = 0; i < list.Count; i++) if (Names.AsciiEqual(identifier, list[i])) return true;
            }
            else
            {
                foreach (string name in names) if (Names.AsciiEqual(identifier, name)) return true;
            }
            return false;
        }
        private sealed class ChannelComponent : ComponentValue
        {
            internal ChannelComponent(CssToken token, CssNumericType type) { Token = token; Type = type; }
            internal CssToken Token { get; }
            internal CssNumericType Type { get; }
            public override int Line => Token.Line;
            public override int Column => Token.Column;
        }
    }
}
