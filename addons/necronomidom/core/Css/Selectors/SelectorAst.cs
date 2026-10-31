using System;
using System.Collections.Generic;
using System.Text;

namespace NecronomiDom.Css.Selectors
{
    /// <summary>Selectors 4 § 17 lexicographically ordered specificity.</summary>
    public readonly record struct Specificity(int A, int B, int C) : IComparable<Specificity>
    {
        public int CompareTo(Specificity other) => A != other.A ? A.CompareTo(other.A) : B != other.B ? B.CompareTo(other.B) : C.CompareTo(other.C);
        public static Specificity operator +(Specificity a, Specificity b) => new Specificity(a.A + b.A, a.B + b.B, a.C + b.C);
        public static Specificity Max(Specificity a, Specificity b) => a.CompareTo(b) >= 0 ? a : b;
    }
    /// <summary>Selectors 4 § 15 combinators connecting compounds.</summary>
    public enum Combinator { Descendant, Child, AdjacentSibling, GeneralSibling, Column }
    /// <summary>Selectors 4 § 15 selector parser context and namespace bindings.</summary>
    public sealed record SelectorParseOptions(bool Forgiving = false, bool Relative = false, bool AllowNesting = true, bool AllowPseudoElements = true, IReadOnlyDictionary<string, string>? Namespaces = null, bool InsideHas = false, bool IgnoreSubjectDefaultNamespace = false)
    {
        internal string? SourceText { get; init; }
        internal bool CompoundOnly { get; init; }
        internal bool PartArgument { get; init; }
    }
    /// <summary>Selectors 4 § 3.1 selector list.</summary>
    public sealed class SelectorList
    {
        public SelectorList(IReadOnlyList<ComplexSelector> selectors)
        {
            Selectors = selectors;
            foreach (var selector in selectors)
                foreach (var compound in selector.Compounds)
                    foreach (var simple in compound.SimpleSelectors)
                        if (simple is InvalidSelector ||
                            simple is PseudoClassSelector pseudo && (pseudo.Arguments?.HasInvalidSelectors == true || pseudo.Nth?.Of?.HasInvalidSelectors == true) ||
                            simple is PseudoElementSelector element && element.Arguments?.HasInvalidSelectors == true)
                        { HasInvalidSelectors = true; return; }
        }
        public IReadOnlyList<ComplexSelector> Selectors { get; }
        /// <summary>CSS Conditional 4 selector() rejects invalid branches even when forgiving matching retains the rest.</summary>
        public bool HasInvalidSelectors { get; }
        public Specificity Specificity
        {
            get { var result = default(Specificity); foreach (var selector in Selectors) result = Specificity.Max(result, selector.Specificity); return result; }
        }
        public string Serialize()
        {
            var result = new StringBuilder();
            var first = true;
            foreach (var selector in Selectors)
            {
                if (selector.Compounds.Count == 1 && selector.Compounds[0].SimpleSelectors.Count == 1 &&
                    selector.Compounds[0].SimpleSelectors[0] is InvalidSelector { ContainsNesting: false }) continue;
                if (!first) result.Append(", ");
                first = false;
                result.Append(selector.Serialize());
            }
            return result.ToString();
        }
    }
    /// <summary>Selectors 4 § 3.1 complex or relative selector.</summary>
    public sealed class ComplexSelector
    {
        public ComplexSelector(IReadOnlyList<CompoundSelector> compounds, IReadOnlyList<Combinator> combinators, Combinator? leadingCombinator = null)
        { Compounds = compounds; Combinators = combinators; LeadingCombinator = leadingCombinator; }
        public IReadOnlyList<CompoundSelector> Compounds { get; }
        public IReadOnlyList<Combinator> Combinators { get; }
        public Combinator? LeadingCombinator { get; }
        public Specificity Specificity
        {
            get { var result = default(Specificity); foreach (var compound in Compounds) result += compound.Specificity; return result; }
        }
        public string Serialize()
        {
            var result = new StringBuilder();
            if (LeadingCombinator.HasValue && LeadingCombinator != Combinator.Descendant) result.Append(Symbol(LeadingCombinator.Value)).Append(' ');
            for (var i = 0; i < Compounds.Count; i++)
            {
                if (i > 0) result.Append(Combinators[i - 1] == Combinator.Descendant ? " " : " " + Symbol(Combinators[i - 1]) + " ");
                result.Append(Compounds[i].Serialize());
            }
            return result.ToString();
        }
        private static string Symbol(Combinator c) => c switch { Combinator.Child => ">", Combinator.AdjacentSibling => "+", Combinator.GeneralSibling => "~", Combinator.Column => "||", _ => " " };
    }
    /// <summary>Selectors 4 § 3.1 compound selector.</summary>
    public sealed class CompoundSelector
    {
        public CompoundSelector(IReadOnlyList<SimpleSelector> simpleSelectors) => SimpleSelectors = simpleSelectors;
        public IReadOnlyList<SimpleSelector> SimpleSelectors { get; }
        /// <summary>Implicit universal selector's namespace constraint; null means unrestricted.</summary>
        public string? NamespaceURI { get; init; }
        internal bool PreserveUniversal { get; init; }
        public Specificity Specificity
        {
            get { var result = default(Specificity); foreach (var simple in SimpleSelectors) result += simple.Specificity; return result; }
        }
        public string Serialize()
        {
            var result = new StringBuilder();
            foreach (var simple in SimpleSelectors)
                if (PreserveUniversal || SimpleSelectors.Count == 1 || simple is not UniversalSelector { NamespacePrefix: null }) result.Append(simple.Serialize());
            return result.ToString();
        }
    }
    /// <summary>Selectors 4 simple selector, with CSSOM § 6.3 serialization.</summary>
    public abstract record SimpleSelector
    {
        public abstract Specificity Specificity { get; }
        public abstract string Serialize();
        protected static string Ident(string text) => Syntax.CssSerialization.SerializeIdentifier(text);
        protected static string Prefix(string? prefix) => prefix == null ? "" : (prefix == "*" ? "*" : Ident(prefix)) + "|";
    }
}
