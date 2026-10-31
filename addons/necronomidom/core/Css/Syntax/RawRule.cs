using System.Collections.Generic;

namespace NecronomiDom.Css.Syntax
{
    /// <summary>CSS Syntax 3 § 5 raw rule, before contextual grammar validation.</summary>
    public abstract record RawRule(IReadOnlyList<ComponentValue> Prelude);
    /// <summary>CSS Syntax 3 § 5.4.2 at-rule, with optional block.</summary>
    public sealed record RawAtRule(string Name, IReadOnlyList<ComponentValue> Prelude, SimpleBlock? Block) : RawRule(Prelude);
    /// <summary>CSS Syntax 3 § 5.4.3 qualified rule.</summary>
    public sealed record RawQualifiedRule(IReadOnlyList<ComponentValue> Prelude, SimpleBlock Block) : RawRule(Prelude);
    /// <summary>CSS Syntax 3 § 5.4.6 declaration with importance removed from its value.</summary>
    public sealed record RawDeclaration(string Name, IReadOnlyList<ComponentValue> Value, bool Important, int Line, int Column);
    /// <summary>CSS Syntax 3 § 5.4.4 ordered block contents; items are declarations or nested rules.</summary>
    public sealed class CssBlockContents
    {
        public CssBlockContents(IReadOnlyList<object> items) => Items = items;
        public IReadOnlyList<object> Items { get; }
    }
}
