using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    /// <summary>CSS-wide keywords (CSS Cascade 5 § 7.3, CSS Values 4 § 4.1.1).</summary>
    public enum CssWideKeywordKind { Initial, Inherit, Unset, Revert, RevertLayer }

    /// <summary>A CSS-wide keyword as a whole declaration value.</summary>
    public sealed class CssWideKeyword : CssValue
    {
        private CssWideKeyword(CssWideKeywordKind kind) => Kind = kind;

        public static readonly CssWideKeyword Initial = new(CssWideKeywordKind.Initial);
        public static readonly CssWideKeyword Inherit = new(CssWideKeywordKind.Inherit);
        public static readonly CssWideKeyword Unset = new(CssWideKeywordKind.Unset);
        public static readonly CssWideKeyword Revert = new(CssWideKeywordKind.Revert);
        public static readonly CssWideKeyword RevertLayer = new(CssWideKeywordKind.RevertLayer);

        public CssWideKeywordKind Kind { get; }

        public static CssWideKeyword? FromIdent(string ident)
        {
            if (NecronomiDom.Dom.Names.AsciiEqual(ident, "initial")) return Initial;
            if (NecronomiDom.Dom.Names.AsciiEqual(ident, "inherit")) return Inherit;
            if (NecronomiDom.Dom.Names.AsciiEqual(ident, "unset")) return Unset;
            if (NecronomiDom.Dom.Names.AsciiEqual(ident, "revert")) return Revert;
            if (NecronomiDom.Dom.Names.AsciiEqual(ident, "revert-layer")) return RevertLayer;
            return null;
        }

        public static bool IsWideKeyword(string ident) => FromIdent(ident) != null;

        public override void AppendTo(StringBuilder sb)
        {
            sb.Append(Kind switch
            {
                CssWideKeywordKind.Initial => "initial",
                CssWideKeywordKind.Inherit => "inherit",
                CssWideKeywordKind.Unset => "unset",
                CssWideKeywordKind.Revert => "revert",
                _ => "revert-layer",
            });
        }
    }

    /// <summary>
    /// A custom property value (CSS Variables 1 § 2): the raw component values, serialized per CSSOM as the token
    /// sequence with leading/trailing whitespace removed.
    /// </summary>
    public sealed class CssUnparsedValue : CssValue
    {
        public CssUnparsedValue(IReadOnlyList<ComponentValue> tokens)
        {
            Tokens = TokenText.Trim(tokens);
        }

        public IReadOnlyList<ComponentValue> Tokens { get; }

        public override void AppendTo(StringBuilder sb) => CssTokenSerializer.AppendOriginal(sb, Tokens);
    }

    /// <summary>
    /// A declaration value containing a substitution function (<c>var() env() attr()</c>, CSS Variables 1
    /// § 3, CSS Values 5 § 7): validity is decided at computed-value time. For a shorthand every longhand gets a
    /// pending value with <see cref="Shorthand"/> set; such longhands serialize as the empty string (CSSOM).
    /// </summary>
    public sealed class CssPendingSubstitutionValue : CssValue
    {
        public CssPendingSubstitutionValue(IReadOnlyList<ComponentValue> tokens, string? shorthand)
        {
            Tokens = TokenText.Trim(tokens);
            Shorthand = shorthand;
            SourceText = CssTokenSerializer.SerializeOriginal(Tokens);
        }

        public IReadOnlyList<ComponentValue> Tokens { get; }
        public string? Shorthand { get; }

        public override void AppendTo(StringBuilder sb)
        {
            if (Shorthand == null) sb.Append(SourceText);
        }

        /// <summary>The token text regardless of <see cref="Shorthand"/> (what the shorthand serializes to).</summary>
        public string SourceText { get; }
    }

    /// <summary>Chrome number formatting for specified values (up to 6 significant digits, via CSSOM serialization).</summary>
    internal static class NumberFormat
    {
        public static string Format(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "infinity";
            if (double.IsNegativeInfinity(value)) return "-infinity";
            return CssSerialization.SerializeNumber(value);
        }
    }

    /// <summary>Whitespace trimming helpers over component value lists.</summary>
    internal static class TokenText
    {
        public static bool IsWhitespace(ComponentValue v) => v is PreservedToken t && t.Token.Kind == CssTokenKind.Whitespace;

        public static IReadOnlyList<ComponentValue> Trim(IReadOnlyList<ComponentValue> tokens)
        {
            int start = 0;
            int end = tokens.Count;
            while (start < end && IsWhitespace(tokens[start])) start++;
            while (end > start && IsWhitespace(tokens[end - 1])) end--;
            if (start == 0 && end == tokens.Count) return tokens;
            var list = new List<ComponentValue>(end - start);
            for (int i = start; i < end; i++) list.Add(tokens[i]);
            return list;
        }

        public static List<ComponentValue> WithoutWhitespace(IReadOnlyList<ComponentValue> tokens)
        {
            var list = new List<ComponentValue>(tokens.Count);
            foreach (ComponentValue v in tokens)
            {
                if (!IsWhitespace(v)) list.Add(v);
            }

            return list;
        }
    }
}
