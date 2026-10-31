using System;
using NecronomiDom.Dom;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Math
{
    /// <summary>CSS Values 4 §§ 10.8–10.9 math grammar and dimensional type checking.</summary>
    public static partial class CssMathParser
    {
        public static bool TryParse(FunctionValue function, out CssValue? value) => TryParse(function, (CssNumericDimension?)null, out value);
        /// <summary>Parse with an explicit dimensional context; Number is the profile's number-percentage production.</summary>
        public static bool TryParse(FunctionValue function, CssNumericDimension? percentageHint, out CssValue? value)
            => TryParseCore(function, percentageHint, null, out value);
        /// <summary>Normalize with the consuming numeric production's explicit percentage magnitude scale.</summary>
        public static bool TryParse(FunctionValue function, CssMathEvaluationContext specifiedContext, out CssValue? value)
            => TryParseCore(function, specifiedContext.PercentageHint, specifiedContext, out value);
        public static bool TryParse(FunctionValue function, CssNumericDimension? percentageHint,
            double percentageBasis, out CssValue? value) =>
            TryParse(function, new CssMathEvaluationContext(percentageBasis: percentageBasis, percentageHint: percentageHint), out value);
        private static bool TryParseCore(FunctionValue function, CssNumericDimension? percentageHint,
            CssMathEvaluationContext? specifiedContext, out CssValue? value)
        {
            value = null;
            if (percentageHint == CssNumericDimension.Percentage) percentageHint = null;
            if (!ParseTypedRoot(function, percentageHint, out CssMathExpression? root, out bool rawPercentage) || root == null) return false;
            CssMathEvaluationContext specified = percentageHint == CssNumericDimension.Number && rawPercentage
                ? CssMathEvaluationContext.PercentageMagnitudes
                : specifiedContext ?? (percentageHint == CssNumericDimension.Number
                    ? CssMathEvaluationContext.NumberPercentage : CssMathEvaluationContext.Specified);
            value = new CssMathValue(CssMathSimplifier.Simplify(root, specified), root.Type,
                Names.AsciiEqual(function.Name, "calc"));
            return true;
        }
        public static bool TryParse(FunctionValue function, IReadOnlyDictionary<string, double> channelSymbols, out CssValue? value)
            => TryParse(function, channelSymbols, null, out value);
        public static bool TryParse(FunctionValue function, IReadOnlyDictionary<string, double> channelSymbols,
            CssNumericDimension? percentageHint, out CssValue? value)
            => TryParse(Substitute(function, channelSymbols), percentageHint, out value);
        private static FunctionValue Substitute(FunctionValue function, IReadOnlyDictionary<string, double> symbols) =>
            new(function.NameToken, SubstituteList(function.Arguments, symbols));
        private static IReadOnlyList<ComponentValue> SubstituteList(IReadOnlyList<ComponentValue> values,
            IReadOnlyDictionary<string, double> symbols)
        {
            var result = new ComponentValue[values.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ComponentValue item = values[i];
                if (item is PreservedToken p && p.Token.Kind == CssTokenKind.Ident && TryGetSymbol(symbols, p.Token.Value, out double number))
                    item = new PreservedToken(new CssToken(CssTokenKind.Number, Number: number, NumberType: CssNumberType.Number));
                else if (item is FunctionValue f) item = Substitute(f, symbols);
                else if (item is SimpleBlock b) item = new SimpleBlock(b.OpenerToken, SubstituteList(b.Value, symbols));
                result[i] = item;
            }
            return result;
        }
        private static bool TryGetSymbol(IReadOnlyDictionary<string, double> symbols, string identifier, out double value)
        {
            if (!symbols.TryGetValue(Names.AsciiLower(identifier), out value)) return false;
            if (symbols is Dictionary<string, double> dictionary &&
                (dictionary.Comparer == StringComparer.Ordinal || dictionary.Comparer == EqualityComparer<string>.Default)) return true;
            foreach (KeyValuePair<string, double> symbol in symbols)
                if (Names.AsciiEqual(identifier, symbol.Key)) { value = symbol.Value; return true; }
            value = 0; return false;
        }
        private sealed class Reader
        {
            private readonly IReadOnlyList<ComponentValue> values;
            private readonly CssNumericDimension? hint;
            private readonly int depth;
            private int position;
            internal Reader(IReadOnlyList<ComponentValue> values, CssNumericDimension? hint, int depth)
            { this.values = values; this.hint = hint; this.depth = depth; }
            internal void SkipSpace()
            {
                while (position < values.Count && values[position] is PreservedToken p && p.Token.Kind == CssTokenKind.Whitespace) position++;
            }
            internal bool End { get { SkipSpace(); return position == values.Count; } }
            internal bool Take(CssTokenKind kind)
            {
                SkipSpace();
                if (position >= values.Count || values[position] is not PreservedToken p || p.Token.Kind != kind) return false;
                position++; return true;
            }
            internal string? TakeIdent()
            {
                SkipSpace();
                if (position >= values.Count || values[position] is not PreservedToken p || p.Token.Kind != CssTokenKind.Ident) return null;
                position++; return Names.AsciiLower(p.Token.Value);
            }
            internal bool IsIdent(string ident)
            {
                SkipSpace(); return position < values.Count && values[position] is PreservedToken p && p.Token.IsIdent(ident);
            }
            internal bool Sum(out CssMathExpression? result)
            {
                if (!Product(out result) || result == null) return false;
                while (true)
                {
                    int beforeSpace = position; SkipSpace();
                    if (position >= values.Count || values[position] is not PreservedToken p || !(p.Token.IsDelim('+') || p.Token.IsDelim('-'))) return true;
                    if (beforeSpace == position) return false;
                    position++;
                    int afterOperator = position; SkipSpace();
                    if (afterOperator == position || !Product(out CssMathExpression? right) || right == null) return false;
                    if (!CssNumericType.Add(result.Type, right.Type, out CssNumericType type)) return false;
                    result = new CssMathOperation(p.Token.Value, new[] { result, right }, type);
                }
            }
            private bool Product(out CssMathExpression? result)
            {
                if (!Atom(out result) || result == null) return false;
                while (true)
                {
                    int saved = position; SkipSpace();
                    if (position >= values.Count || values[position] is not PreservedToken p || !(p.Token.IsDelim('*') || p.Token.IsDelim('/')))
                    { position = saved; return true; }
                    position++;
                    if (!Atom(out CssMathExpression? right) || right == null) return false;
                    if (!CssNumericType.Multiply(result.Type, right.Type, p.Token.IsDelim('/'), out CssNumericType type)) return false;
                    result = new CssMathOperation(p.Token.Value, new[] { result, right }, type);
                }
            }
            private bool Atom(out CssMathExpression? result)
            {
                result = null; SkipSpace();
                if (position >= values.Count || depth > 128) return false;
                ComponentValue item = values[position++];
                if (item is ChannelComponent channel)
                {
                    result = new CssMathNumericLeaf(new CssKeywordValue(Names.AsciiLower(channel.Token.Value)), channel.Type);
                    return true;
                }
                if (item is FunctionValue f)
                    return KnownFunction(Names.AsciiLower(f.Name)) ? ParseFunction(f, hint, depth + 1, out result) :
                        CssMathNumericLeaf.TryParse(f, hint, out result);
                if (item is SimpleBlock b && b.Opener == CssTokenKind.LeftParen)
                {
                    var reader = new Reader(b.Value, hint, depth + 1);
                    return reader.Sum(out result) && reader.End;
                }
                if (item is not PreservedToken p) return false;
                CssToken t = p.Token;
                if (t.Kind == CssTokenKind.Number) { result = new CssMathLiteral(t.Number, "", default); return true; }
                if (t.Kind == CssTokenKind.Percentage)
                {
                    CssNumericType type = CssNumericType.For(CssNumericDimension.Percentage);
                    type = type.Hint(hint ?? CssNumericDimension.Percentage);
                    result = new CssMathLiteral(t.Number, "%", type); return true;
                }
                if (t.Kind == CssTokenKind.Dimension && CssMathUnits.TryGet(t.Unit, out CssNumericDimension dimension, out _))
                { result = new CssMathLiteral(t.Number, Names.AsciiLower(t.Unit), CssNumericType.For(dimension)); return true; }
                if (t.Kind != CssTokenKind.Ident) return false;
                double? constant = Names.AsciiLower(t.Value) switch
                {
                    "e" => System.Math.E, "pi" => System.Math.PI, "infinity" => double.PositiveInfinity,
                    "-infinity" => double.NegativeInfinity, "nan" => double.NaN, _ => null
                };
                if (!constant.HasValue) return false;
                result = new CssMathLiteral(constant.Value, "", default); return true;
            }
        }
    }
}
