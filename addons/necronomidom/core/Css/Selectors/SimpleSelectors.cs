using System.Collections.Generic;
using System.Globalization;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Selectors
{
    /// <summary>Selectors 4 § 5 type selector.</summary>
    public sealed record TypeSelector(string Name, string? NamespacePrefix = null) : SimpleSelector
    {
        public string? NamespaceURI { get; init; }
        public override Specificity Specificity => new Specificity(0, 0, 1);
        public override string Serialize() => Prefix(NamespacePrefix) + Ident(Name);
    }
    /// <summary>Selectors 4 § 5.2 universal selector.</summary>
    public sealed record UniversalSelector(string? NamespacePrefix = null) : SimpleSelector
    {
        public string? NamespaceURI { get; init; }
        public override Specificity Specificity => default;
        public override string Serialize() => Prefix(NamespacePrefix) + "*";
    }
    /// <summary>Selectors 4 § 6.6 ID selector.</summary>
    public sealed record IdSelector(string Id) : SimpleSelector
    { public override Specificity Specificity => new Specificity(1, 0, 0); public override string Serialize() => "#" + Ident(Id); }
    /// <summary>Selectors 4 § 6.5 class selector.</summary>
    public sealed record ClassSelector(string ClassName) : SimpleSelector
    { public override Specificity Specificity => new Specificity(0, 1, 0); public override string Serialize() => "." + Ident(ClassName); }
    /// <summary>Selectors 4 § 6 attribute matching operators.</summary>
    public enum AttributeOperator { Exists, Equals, Includes, DashMatch, Prefix, Suffix, Substring }
    /// <summary>Selectors 4 § 6.3 attribute value case flag.</summary>
    public enum CaseSensitivity { Default, Insensitive, Sensitive }
    /// <summary>Selectors 4 § 6 attribute selector.</summary>
    public sealed record AttributeSelector(string Name, string? NamespacePrefix = null, AttributeOperator Operator = AttributeOperator.Exists, string? Value = null, CaseSensitivity CaseSensitivity = CaseSensitivity.Default) : SimpleSelector
    {
        public string? NamespaceURI { get; init; } = "";
        public override Specificity Specificity => new Specificity(0, 1, 0);
        public override string Serialize()
        {
            var op = Operator switch { AttributeOperator.Equals => "=", AttributeOperator.Includes => "~=", AttributeOperator.DashMatch => "|=", AttributeOperator.Prefix => "^=", AttributeOperator.Suffix => "$=", AttributeOperator.Substring => "*=", _ => "" };
            return "[" + (NamespacePrefix == "" ? "" : Prefix(NamespacePrefix)) + Ident(Name) + op + (Operator == AttributeOperator.Exists ? "" : CssSerialization.SerializeString(Value ?? "")) +
                (CaseSensitivity == CaseSensitivity.Insensitive ? " i" : CaseSensitivity == CaseSensitivity.Sensitive ? " s" : "") + "]";
        }
    }
    /// <summary>CSS Syntax 3 § 6 An+B expression, with Selectors 4 optional of-list.</summary>
    public sealed record NthExpression(int A, int B, SelectorList? Of = null)
    {
        public string Serialize()
        {
            var a = A.ToString(CultureInfo.InvariantCulture);
            var b = B.ToString(CultureInfo.InvariantCulture);
            var expression = A == 0 ? b : (A == 1 ? "n" : A == -1 ? "-n" : a + "n") + (B == 0 ? "" : B > 0 ? "+" + b : b);
            return expression + (Of == null ? "" : " of " + Of.Serialize());
        }
    }
    /// <summary>Selectors 4 pseudo-class, retaining its parsed selector or An+B argument.</summary>
    public sealed record PseudoClassSelector(string Name, SelectorList? Arguments = null, string? Argument = null, NthExpression? Nth = null) : SimpleSelector
    {
        public IReadOnlyList<string>? LanguageRanges { get; init; }
        /// <summary>Decoded identifier argument for native custom-state matching; CSS serialization remains in Argument.</summary>
        public string? DecodedArgument { get; init; }
        public override Specificity Specificity => Name == "where" ? default : Name == "is" || Name == "not" || Name == "has" ? Arguments?.Specificity ?? default :
            new Specificity(0, 1, 0) + (Arguments?.Specificity ?? Nth?.Of?.Specificity ?? default);
        public override string Serialize() => ":" + Name + (Nth != null ? "(" + Nth.Serialize() + ")" : Arguments != null ? "(" + Arguments.Serialize() + ")" : Argument != null ? "(" + Argument + ")" : "");
    }
    /// <summary>Selectors 4 § 3.6 pseudo-element, including shadow and customizable-select arguments.</summary>
    public sealed record PseudoElementSelector(string Name, SelectorList? Arguments = null, string? Argument = null) : SimpleSelector
    {
        public override Specificity Specificity => new Specificity(0, 0, 1) + (Arguments?.Specificity ?? default);
        public override string Serialize() => "::" + Name + (Arguments != null ? "(" + Arguments.Serialize() + ")" : Argument != null ? "(" + Argument + ")" : "");
    }
    /// <summary>CSS Nesting 1 § 4 nesting selector; parent specificity is supplied by the cascade.</summary>
    public sealed record NestingSelector : SimpleSelector
    { public override Specificity Specificity => default; public override string Serialize() => "&"; }
    /// <summary>A retained invalid forgiving-list branch, contributing zero specificity and never matching.</summary>
    public sealed record InvalidSelector(string Text) : SimpleSelector
    {
        internal bool ContainsNesting { get; init; }
        public override Specificity Specificity => default;
        public override string Serialize() => Text;
    }
}
