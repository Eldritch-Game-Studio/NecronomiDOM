namespace NecronomiDom.Css.Media
{
    /// <summary>Media Queries 5 custom-media boolean definitions (not arbitrary general-enclosed syntax).</summary>
    public sealed record BooleanCondition(bool Value) : QueryCondition
    { public override string Serialize() => Value ? "true" : "false"; }
    /// <summary>A custom-media reference retains media types, modifiers and complete query-list semantics.</summary>
    public sealed record MediaQueryCondition(MediaQueryList Queries) : QueryCondition
    { public override string Serialize() => Queries.Serialize(); }
}
