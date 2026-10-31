using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Css.Values.Math
{
    /// <summary>CSS Values 4 § 10.10 immutable typed calculation tree.</summary>
    public abstract class CssMathExpression
    {
        protected CssMathExpression(CssNumericType type) => Type = type;
        public CssNumericType Type { get; }
        public abstract bool TryEvaluate(CssMathEvaluationContext context, out double value);
        internal abstract void AppendTo(StringBuilder builder, int precedence);
    }

    /// <summary>CSS Values 4 § 10.10 calculation-tree numeric leaf.</summary>
    public sealed class CssMathLiteral : CssMathExpression
    {
        public CssMathLiteral(double value, string unit, CssNumericType type, bool isUnbounded = false) : base(type)
        { Value = value; Unit = unit; IsUnbounded = isUnbounded; }
        public double Value { get; }
        public string Unit { get; }
        public bool IsUnbounded { get; }
        public override bool TryEvaluate(CssMathEvaluationContext context, out double value)
        {
            value = Value;
            if (IsUnbounded) return true;
            if (double.IsNaN(Value)) return true;
            if (Value == 0 && Unit != "%" && CssMathUnits.TryGet(Unit, out _, out _)) return true;
            if (Unit == "%") return context.TryEvaluatePercentage(Value, out value);
            if (!context.TryScale(Unit, out double scale)) return false;
            value *= scale; return true;
        }
        internal override void AppendTo(StringBuilder builder, int precedence)
        {
            if (IsUnbounded) { builder.Append("none"); return; }
            AppendNumber(builder, Value, Unit);
        }
        internal static void AppendNumber(StringBuilder builder, double value, string unit)
        {
            builder.Append(NumberFormat.Format(value));
            if (!double.IsFinite(value))
            {
                if (unit.Length != 0) builder.Append(" * 1").Append(unit);
            }
            else builder.Append(unit);
        }
    }

    /// <summary>CSS Values 4 §§ 10.1–10.6 arithmetic or mathematical-function node.</summary>
    public sealed class CssMathOperation : CssMathExpression
    {
        public CssMathOperation(string name, IReadOnlyList<CssMathExpression> arguments, CssNumericType type,
            string? strategy = null) : base(type)
        { Name = name; Arguments = arguments; Strategy = strategy; }
        public string Name { get; }
        public IReadOnlyList<CssMathExpression> Arguments { get; }
        public string? Strategy { get; }
        public override bool TryEvaluate(CssMathEvaluationContext context, out double value)
        {
            value = 0;
            double[]? rented = Arguments.Count > 128 ? ArrayPool<double>.Shared.Rent(Arguments.Count) : null;
            Span<double> values = rented == null ? stackalloc double[Arguments.Count] : rented.AsSpan(0, Arguments.Count);
            try
            {
                bool unresolved = false;
                for (int i = 0; i < Arguments.Count; i++)
                {
                    if (!Arguments[i].TryEvaluate(context, out values[i])) unresolved = true;
                    else if (double.IsNaN(values[i])) { value = double.NaN; return true; }
                }
                if (unresolved || Strategy == "line-width" && !context.DevicePixelRatio.HasValue) return false;
                value = Strategy == "line-width"
                    ? CssMathSemantics.LineWidth(values, context.DevicePixelRatio!.Value)
                    : CssMathSemantics.Evaluate(Name, values, Arguments[0].Type, Strategy);
                return true;
            }
            finally
            {
                if (rented != null) ArrayPool<double>.Shared.Return(rented);
            }
        }
        internal override void AppendTo(StringBuilder builder, int precedence) =>
            CssMathSerialization.AppendOperation(builder, this, precedence);
    }

    /// <summary>CSS Values 4 § 10 specified math value with explicit typed evaluation and simplification.</summary>
    public partial class CssMathValue : CssValue
    {
        public CssMathValue(CssMathExpression root, CssNumericType type, bool wasCalc = false)
            : this(root, type, wasCalc, false, double.NegativeInfinity, double.PositiveInfinity) { }
        public virtual CssMathExpression Root { get; }
        public virtual CssNumericType Type { get; }
        public bool WasCalc { get; }
        public bool TryEvaluate(CssMathEvaluationContext context, out double value) => Root.TryEvaluate(context, out value);
        public bool TryGetConstant(out double value) => Root.TryEvaluate(CssMathEvaluationContext.Specified, out value);
        public CssMathValue Simplify(CssMathEvaluationContext context)
        {
            if (context.PercentageHint == null && Type.PercentageHint is not null and not CssNumericDimension.Percentage)
                context = new CssMathEvaluationContext(context.ResolveUnit, context.PercentageBasis, Type.PercentageHint,
                    context.DevicePixelRatio, context.ResolveNumericValue, context.IntrinsicSize);
            CssMathExpression root = CssMathSimplifier.Simplify(Root, context, true);
            if (root is CssMathLiteral { Unit: "%" } percentage && !percentage.Type.IsPercentage)
                root = new CssMathLiteral(percentage.Value, "%", CssNumericType.For(CssNumericDimension.Percentage).Hint(CssNumericDimension.Percentage));
            return new CssMathValue(root, Type, WasCalc, IsIntegerTarget, GrammarMinimum, GrammarMaximum);
        }
        public bool Fits(string type) => type switch
        {
            "number" or "integer" => Type.IsNumber,
            "percentage" => Type.IsPercentage,
            "length" => Type.IsDimension(CssNumericDimension.Length),
            "length-percentage" => Type.Matches(CssNumericDimension.Length, true),
            "number-percentage" => Type.MatchesNumber(CssNumericDimension.Number) || Type.IsPercentage,
            "angle" => Type.IsDimension(CssNumericDimension.Angle),
            "angle-percentage" => Type.Matches(CssNumericDimension.Angle, true),
            "time" => Type.IsDimension(CssNumericDimension.Time),
            "frequency" => Type.IsDimension(CssNumericDimension.Frequency),
            "resolution" => Type.IsDimension(CssNumericDimension.Resolution),
            "flex" => Type.IsDimension(CssNumericDimension.Flex), _ => false
        };
        public override void AppendTo(StringBuilder builder)
        {
            bool wrap = WasCalc || Root is CssMathLiteral || Root is CssMathOperation op && CssMathSerialization.IsArithmetic(op.Name);
            if (wrap) builder.Append("calc(");
            Root.AppendTo(builder, 0);
            if (wrap) builder.Append(')');
        }
    }
}
