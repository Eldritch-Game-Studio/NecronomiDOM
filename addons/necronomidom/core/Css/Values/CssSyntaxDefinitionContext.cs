namespace NecronomiDom.Css.Values
{
    /// <summary>Consumer-specific syntax definitions: Properties and Values API 1 § 5 and CSS Values 5 §§ 3.3, 8.7.</summary>
    public enum CssSyntaxDefinitionContext
    {
        /// <summary>A registered custom property's supported syntax component names.</summary>
        Registration,
        /// <summary>Typed attr() syntax definitions; URL types are forbidden.</summary>
        Attribute,
    }
}
