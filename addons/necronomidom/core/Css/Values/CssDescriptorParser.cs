using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Registry;

namespace NecronomiDom.Css.Values
{
    /// <summary>Descriptor grammars for Fonts 4, Counter Styles 3 and Properties and Values API 1.</summary>
    public static class CssDescriptorParser
    {
        public static bool TryParse(string atRuleName, string descriptor, IReadOnlyList<ComponentValue> value, out CssValue? parsed)
        {
            parsed = null;
            value = TokenText.Trim(value);
            if (!CssPropertyParser.ValidDeclaration(value)) return false;
            string rule = NecronomiDom.Dom.Names.AsciiLower(atRuleName.TrimStart('@'));
            descriptor = NecronomiDom.Dom.Names.AsciiLower(descriptor);
            if (rule == "position-try")
            {
                if (!IsPositionTryProperty(descriptor)) return false;
                ParsedProperty? result = CssPropertyParser.Parse(descriptor, value);
                if (result == null) return false;
                CssValue first = result.Longhands[0].Value;
                if (result.Longhands.Count == 1 || first is CssWideKeyword) parsed = first;
                else if (first is CssPendingSubstitutionValue pending) parsed = new CssPendingSubstitutionValue(pending.Tokens, null);
                else if (ShorthandSerializer.TrySerialize(descriptor, name =>
                {
                    foreach (var pair in result.Longhands) if (pair.Longhand == name) return pair.Value;
                    return null;
                }, out string text)) parsed = CssValueParser.Parse(CssParser.ParseComponentValueList(text));
                return parsed != null;
            }
            if (rule is "stylistic" or "styleset" or "character-variant" or "swash" or "ornaments" or "annotation")
            {
                if (!CssTypeParser.TryParse("custom-ident", CssParser.ParseComponentValueList(descriptor), out _)) return false;
                string featureSyntax = rule == "styleset" ? "<integer [0,∞]>+" : rule == "character-variant"
                    ? "<integer [0,∞]>{1,2}" : "<integer [0,∞]>";
                GrammarMatch? features = new GrammarMatcher(value, descriptor).Match(GrammarParser.Parse(featureSyntax));
                parsed = features?.Value;
                return parsed != null;
            }
            if (!WebrefData.Current.AtRules.TryGetValue(rule, out Dictionary<string, WebrefDescriptor>? descriptors)
                || !descriptors.TryGetValue(descriptor, out WebrefDescriptor? definition) || definition.Syntax == null) return false;
            GrammarMatch? match = new GrammarMatcher(value, descriptor).Match(GrammarParser.Parse(definition.Syntax));
            if (match == null) return false;
            if (!CssDescriptorConstraints.Check(rule, descriptor, match.Value)) return false;
            if (rule == "property" && descriptor == "syntax" && match.Value is CssStringValue syntax
                && !CssRegistrationSyntax.IsValid(syntax.Value)) return false;
            if (rule == "font-face" && !CssValueConstraints.Check(descriptor, value)) return false;
            parsed = rule == "property" && descriptor == "initial-value"
                ? new CssUnparsedValue(value) : CssSpecifiedValueNormalizer.Normalize(descriptor, match.Value);
            return true;
        }

        internal static bool IsPositionTryProperty(string name) => name is "margin" or "margin-top" or "margin-right" or "margin-bottom"
            or "margin-left" or "margin-inline" or "margin-block" or "margin-inline-start" or "margin-inline-end" or "margin-block-start" or "margin-block-end"
            or "inset" or "inset-inline" or "inset-block" or "inset-inline-start" or "inset-inline-end" or "inset-block-start" or "inset-block-end"
            or "top" or "right" or "bottom" or "left"
            or "width" or "height" or "min-width" or "max-width" or "min-height" or "max-height" or "inline-size" or "block-size"
            or "min-inline-size" or "max-inline-size" or "min-block-size" or "max-block-size" or "align-self" or "justify-self" or "place-self"
            or "position-anchor" or "position-area";
    }

    /// <summary>Consumer syntax strings, Properties and Values API 1 § 5 and CSS Values 5 § 3.3; distinct from full definition syntax.</summary>
    public static class CssRegistrationSyntax
    {
        private static readonly TypeNode Universal = new("declaration-value", false, double.NegativeInfinity, double.PositiveInfinity);
        public static bool TryParseValue(string syntax, IReadOnlyList<ComponentValue> value, out CssValue? parsed,
            CssSyntaxDefinitionContext context = CssSyntaxDefinitionContext.Registration)
        {
            parsed = null;
            if (!Compile(syntax, context, true, out GrammarNode? grammar) || !CssPropertyParser.ValidDeclaration(value)) return false;
            if (grammar is TypeNode { Name: "declaration-value" })
            {
                parsed = new CssUnparsedValue(value);
                return true;
            }
            if (!CssValueConstraints.Check("", value)) return false;
            GrammarMatch? match = new GrammarMatcher(value, "").Match(grammar!);
            parsed = match == null ? null : CssSpecifiedValueNormalizer.Normalize("", match.Value);
            return parsed != null;
        }

        public static bool IsValid(string syntax, CssSyntaxDefinitionContext context = CssSyntaxDefinitionContext.Registration)
            => Compile(syntax, context, false, out _);

        private static bool Compile(string syntax, CssSyntaxDefinitionContext context, bool build, out GrammarNode? grammar)
        {
            grammar = null;
            int pos = 0;
            Space(syntax, ref pos);
            if (pos == syntax.Length) return false;
            if (syntax[pos] == '*')
            {
                pos++; Space(syntax, ref pos);
                if (pos != syntax.Length) return false;
                if (build) grammar = Universal;
                return true;
            }
            List<GrammarNode>? alternatives = build ? new List<GrammarNode>() : null;
            while (pos < syntax.Length)
            {
                GrammarNode? component = null;
                bool transformList = false;
                if (syntax[pos] == '<')
                {
                    int end = syntax.IndexOf('>', pos + 1);
                    if (end < 0) return false;
                    string type = syntax[(pos + 1)..end];
                    if (type is not ("length" or "number" or "percentage" or "length-percentage" or "color" or "image" or "url" or "integer"
                        or "angle" or "time" or "resolution" or "transform-function" or "transform-list" or "custom-ident" or "string")) return false;
                    if (context == CssSyntaxDefinitionContext.Attribute && type == "url") return false;
                    transformList = type == "transform-list";
                    if (build) component = new TypeNode(type, false, double.NegativeInfinity, double.PositiveInfinity);
                    pos = end + 1;
                }
                else
                {
                    int start = pos;
                    NameEnd(syntax, ref pos);
                    if (start == pos) return false;
                    IReadOnlyList<CssToken> tokens = CssTokenizer.Tokenize(syntax[start..pos]);
                    CssToken? name = null;
                    foreach (CssToken token in tokens)
                    {
                        if (token.Kind == CssTokenKind.Eof) continue;
                        if (name != null || token.Kind != CssTokenKind.Ident || CssWideKeyword.IsWideKeyword(token.Value) || token.IsIdent("default")) return false;
                        name = token;
                    }
                    if (name == null) return false;
                    if (build) component = new KeywordNode(NecronomiDom.Dom.Names.AsciiLower(name.Value.Value));
                }
                Space(syntax, ref pos);
                if (pos < syntax.Length && syntax[pos] is '+' or '#')
                {
                    if (transformList) return false;
                    if (build) component = new MultiplierNode(component!, 1, int.MaxValue, syntax[pos] == '#');
                    pos++; Space(syntax, ref pos);
                }
                alternatives?.Add(component!);
                if (pos == syntax.Length)
                {
                    if (build) grammar = alternatives!.Count == 1 ? alternatives[0] : new CombinationNode(CombinatorKind.OneOf, alternatives);
                    return true;
                }
                if (syntax[pos++] != '|') return false;
                Space(syntax, ref pos);
            }
            return false;
        }

        private static void Space(string syntax, ref int pos)
        {
            while (pos < syntax.Length && CssTokenizer.IsWhitespace(syntax[pos])) pos++;
        }
        private static void NameEnd(string syntax, ref int pos)
        {
            while (pos < syntax.Length && !CssTokenizer.IsWhitespace(syntax[pos]) && syntax[pos] is not ('|' or '+' or '#' or '*' or '<' or '>'))
            {
                if (syntax[pos++] != '\\') continue;
                if (pos == syntax.Length) break;
                int digits = 0;
                while (pos < syntax.Length && digits < 6 && syntax[pos] is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')
                { pos++; digits++; }
                if (digits == 0) pos++;
                else if (pos < syntax.Length && CssTokenizer.IsWhitespace(syntax[pos]))
                {
                    if (syntax[pos++] == '\r' && pos < syntax.Length && syntax[pos] == '\n') pos++;
                }
            }
        }
    }
}
