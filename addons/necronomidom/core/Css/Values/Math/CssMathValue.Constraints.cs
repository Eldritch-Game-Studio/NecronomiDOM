namespace NecronomiDom.Css.Values.Math
{
    /// <summary>CSS Values 4 §§ 10.9/10.11 consuming grammar constraints, applied only when a calculation resolves.</summary>
    public partial class CssMathValue
    {
        private CssMathValue(CssMathExpression root, CssNumericType type, bool wasCalc,
            bool integerTarget, double minimum, double maximum)
        {
            Root = root; Type = type; WasCalc = wasCalc;
            IsIntegerTarget = integerTarget; GrammarMinimum = minimum; GrammarMaximum = maximum;
        }
        public bool IsIntegerTarget { get; }
        /// <summary>Declared lower bound in the consuming numeric production's canonical units.</summary>
        public double GrammarMinimum { get; }
        /// <summary>Declared upper bound in the consuming numeric production's canonical units.</summary>
        public double GrammarMaximum { get; }
        public CssMathValue WithGrammarConstraints(bool integerTarget, double minimum, double maximum) =>
            IsIntegerTarget == integerTarget && GrammarMinimum == minimum && GrammarMaximum == maximum
                ? this : new CssMathValue(Root, Type, WasCalc, integerTarget, minimum, maximum);
    }
}
