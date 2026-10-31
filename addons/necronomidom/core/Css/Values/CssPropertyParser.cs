using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Images;

namespace NecronomiDom.Css.Values
{
    /// <summary>Expanded declaration longhands (CSS Cascade 5 § 3 and CSSOM § 6.7).</summary>
    public sealed class ParsedProperty
    {
        public ParsedProperty(IReadOnlyList<(string Longhand, CssValue Value)> longhands) => Longhands = longhands;
        public IReadOnlyList<(string Longhand, CssValue Value)> Longhands { get; }
    }

    /// <summary>Per-property parsing with no permissive grammar fallback (CSS Syntax 3 § 5, Values 4 § 2).</summary>
    public static class CssPropertyParser
    {
        public static ParsedProperty? Parse(string property, IReadOnlyList<ComponentValue> value)
        {
            value = TokenText.Trim(value);
            if (!ValidDeclaration(value)) return null;
            if (property.StartsWith("--", StringComparison.Ordinal))
            {
                if (property.Length == 2 || !ValidSubstitutions(value, out _)) return null;
                CssValue custom = value.Count == 1 && value[0] is PreservedToken { Token.Kind: CssTokenKind.Ident } ident
                    && CssWideKeyword.FromIdent(ident.Token.Value) is CssWideKeyword wide ? wide : new CssUnparsedValue(value);
                return new ParsedProperty(new[] { (property, custom) });
            }
            PropertyDefinition? definition = CssPropertyRegistry.Get(property);
            if (definition == null || value.Count == 0) return null;
            if (!ValidSubstitutions(value, out bool pending)) return null;
            if (value.Count == 1 && value[0] is PreservedToken { Token.Kind: CssTokenKind.Ident } keyword
                && CssWideKeyword.FromIdent(keyword.Token.Value) is CssWideKeyword global)
                return Broadcast(definition, global);
            if (pending) return Broadcast(definition, new CssPendingSubstitutionValue(value, definition.IsShorthand ? definition.Name : null));
            if (!CssValueConstraints.Check(definition.Name, value)) return null;
            GrammarMatch? match = new GrammarMatcher(value, definition.Name).Match(definition.Grammar);
            if (match == null) return null;
            if (!definition.IsShorthand)
                return new ParsedProperty(new[] { (definition.Name, CssSpecifiedValueNormalizer.Normalize(definition.Name, match.Value)) });
            return ShorthandExpansion.Expand(definition, TokenText.WithoutWhitespace(value), match);
        }

        private static ParsedProperty Broadcast(PropertyDefinition definition, CssValue value)
        {
            if (!definition.IsShorthand) return new ParsedProperty(new[] { (definition.Name, value) });
            var values = new List<(string, CssValue)>();
            foreach (string name in ShorthandExpansion.Leaves(definition)) values.Add((name, value));
            return new ParsedProperty(values);
        }

        internal static bool ValidDeclaration(IReadOnlyList<ComponentValue> tokens)
        {
            foreach (ComponentValue token in tokens)
            {
                if (!ValidCustomToken(token)) return false;
                if (token is PreservedToken p && (p.Token.Kind == CssTokenKind.Semicolon || p.Token.IsDelim('!'))) return false;
            }
            return true;
        }

        internal static bool ValidCustomToken(ComponentValue token)
        {
            if (token is PreservedToken p)
                return p.Token.Kind is not (CssTokenKind.BadString or CssTokenKind.BadUrl or CssTokenKind.RightParen or CssTokenKind.RightBracket or CssTokenKind.RightBrace or CssTokenKind.Eof);
            IReadOnlyList<ComponentValue> children = token is FunctionValue f ? f.Arguments : ((SimpleBlock)token).Value;
            foreach (ComponentValue child in children) if (!ValidCustomToken(child)) return false;
            return true;
        }

        private static bool ValidSubstitutions(IReadOnlyList<ComponentValue> tokens, out bool pending)
        {
            pending = false;
            foreach (ComponentValue token in tokens)
            {
                if (token is PreservedToken) continue;
                IReadOnlyList<ComponentValue> children = token is FunctionValue f ? f.Arguments : ((SimpleBlock)token).Value;
                if (!ValidSubstitutions(children, out bool nested)) return false;
                pending |= nested;
                if (token is not FunctionValue function) continue;
                List<ComponentValue> args = TokenText.WithoutWhitespace(children);
                if (NecronomiDom.Dom.Names.AsciiEqual(function.Name, "var"))
                {
                    if (args.Count == 0 || args[0] is not PreservedToken { Token.Kind: CssTokenKind.Ident } name
                        || !name.Token.Value.StartsWith("--", StringComparison.Ordinal) || name.Token.Value.Length == 2
                        || args.Count > 1 && args[1] is not PreservedToken { Token.Kind: CssTokenKind.Comma }) return false;
                    pending = true;
                }
                else if (NecronomiDom.Dom.Names.AsciiEqual(function.Name, "env"))
                {
                    if (args.Count == 0 || args[0] is not PreservedToken { Token.Kind: CssTokenKind.Ident }) return false;
                    int i = 1;
                    while (i < args.Count && args[i] is PreservedToken { Token.Kind: CssTokenKind.Number, Token.NumberType: CssNumberType.Integer } n && n.Token.Number >= 0) i++;
                    if (i < args.Count && args[i] is not PreservedToken { Token.Kind: CssTokenKind.Comma }) return false;
                    pending = true;
                }
                else if (NecronomiDom.Dom.Names.AsciiEqual(function.Name, "attr"))
                {
                    if (!CssAttrParser.TryParse(function, out _)) return false;
                    pending = true;
                }
            }
            return true;
        }
    }
}
