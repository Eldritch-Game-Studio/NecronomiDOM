using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Selectors;

namespace NecronomiDom.Css.Media
{
    /// <summary>Media Queries 4 § 3 and CSS Conditional 3 § 3 condition syntax tree.</summary>
    public abstract record QueryCondition { public abstract string Serialize(); }
    /// <summary>Conditional query logical negation.</summary>
    public sealed record NotCondition(QueryCondition Condition) : QueryCondition
    { public override string Serialize() => "not " + Condition.Serialize(); }
    /// <summary>Homogeneous conjunction or disjunction; mixing operators requires parentheses.</summary>
    public sealed record JunctionCondition(string Operator, IReadOnlyList<QueryCondition> Conditions) : QueryCondition
    {
        public override string Serialize()
        { var result = new StringBuilder(); foreach (var condition in Conditions) { if (result.Length > 0) result.Append(' ').Append(Operator).Append(' '); result.Append(condition.Serialize()); } return result.ToString(); }
    }
    /// <summary>Parenthesized nested query condition.</summary>
    public sealed record GroupCondition(QueryCondition Condition) : QueryCondition
    { public override string Serialize() => "(" + Condition.Serialize() + ")"; }
    /// <summary>Media Queries 4 § 2 boolean, plain or range feature. Comparisons read in source order.</summary>
    public sealed record FeatureCondition(string Name, string? Value = null, string? Comparison = null, string? LeftValue = null, string? LeftComparison = null) : QueryCondition
    {
        public override string Serialize() => "(" + (LeftValue == null ? "" : LeftValue + " " + LeftComparison + " ") + Name +
            (Value == null ? "" : Comparison == null ? ": " + Value : " " + Comparison + " " + Value) + ")";
    }
    /// <summary>CSS Conditional 3 § 3 declaration support query.</summary>
    public sealed record DeclarationCondition(string Property, string Value) : QueryCondition
    { public override string Serialize() => "(" + Property + ": " + Value + ")"; }
    /// <summary>CSS Conditional 4 selector() support query.</summary>
    public sealed record SelectorCondition(SelectorList Selectors) : QueryCondition
    { public override string Serialize() => "selector(" + Selectors.Serialize() + ")"; }
    /// <summary>CSS Conditional 5 style() or font capability function query.</summary>
    public sealed record FunctionCondition(string Name, string Argument, QueryCondition? Condition = null) : QueryCondition
    {
        public override string Serialize() => Name + "(" + (Condition is DeclarationCondition declaration ? declaration.Property + ": " + declaration.Value : Condition?.Serialize() ?? Argument) + ")";
    }
    /// <summary>Media Queries 4 § 3.1 general-enclosed syntax; evaluates false, preserving forward-compatible syntax.</summary>
    public sealed record GeneralEnclosedCondition(string Text) : QueryCondition
    { public override string Serialize() => Text; }
    /// <summary>Media Queries 4 § 3 single query, including invalid-query replacement.</summary>
    public sealed record MediaQuery(string Type = "all", string? Modifier = null, QueryCondition? Condition = null, bool IsValid = true)
    {
        public string Serialize() => !IsValid ? "not all" : Condition != null && Type == "all" && Modifier == null ? Condition.Serialize() :
            (Modifier == null ? "" : Modifier + " ") + Type + (Condition == null ? "" : " and " + Condition.Serialize());
    }
    /// <summary>Media Queries 4 comma-separated media query list.</summary>
    public sealed class MediaQueryList
    {
        public MediaQueryList(IReadOnlyList<MediaQuery> queries) => Queries = queries;
        public IReadOnlyList<MediaQuery> Queries { get; }
        public string Serialize() { var result = new StringBuilder(); foreach (var query in Queries) { if (result.Length > 0) result.Append(", "); result.Append(query.Serialize()); } return result.ToString(); }
    }
    /// <summary>CSS Conditional 5 container name and query condition.</summary>
    public sealed record ContainerQuery(string? Name, QueryCondition Condition)
    { public string Serialize() => (Name == null ? "" : Syntax.CssSerialization.SerializeIdentifier(Name) + " ") + Condition.Serialize(); }
    /// <summary>Media Queries evaluation hook; parsing does not depend on a device or engine.</summary>
    public interface IMediaEvaluator { bool Evaluate(MediaQueryList queries); }
}
