using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Registry;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values.Grammar
{
    internal sealed partial class GrammarMatcher
    {
        private static readonly ConcurrentDictionary<string, GrammarNode> ParsedSyntaxes = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> BrowserTypes = new(StringComparer.Ordinal)
        {
            ["shadow"] = "<color>? && <length>{2} <length [0,∞]>?",
            ["spread-shadow"] = "<color>? && <length>{2} [ <length [0,∞]> <length>? ]? && inset?",
            ["single-animation"] = "<'animation-duration'> || <easing-function> || <time> || <single-animation-iteration-count> || <single-animation-direction> || <single-animation-fill-mode> || <single-animation-play-state> || [ none | <keyframes-name> ] || <single-animation-timeline>",
            ["timeline-range-name"] = "cover | contain | entry | exit | entry-crossing | exit-crossing",
            ["animation-action"] = "play | pause | reset | play-once | play-forwards | play-backwards | replay",
            ["font-family-name"] = "<string> | <custom-ident>+",
            ["family-name"] = "<string> | <custom-ident>+",
            ["minmax()"] = "minmax( <inflexible-breadth> , <track-breadth> )",
            ["anchor()"] = "anchor( <anchor-name>? && <anchor-side> [ , <length-percentage> ]? )",
            ["anchor-size()"] = "anchor-size( [ <anchor-name> || <anchor-size> ]? [ , <length-percentage> ]? )",
            ["path()"] = "path( [ <'fill-rule'> , ]? <string> )",
            ["polygon()"] = "polygon( [ [ <'fill-rule'> [ round <length [0,∞]> ]? | round <length [0,∞]> ] , ]? [ <length-percentage> <length-percentage> ]# )",
            ["scale()"] = "scale( [ <number> | <percentage> ]#{1,2} )",
            ["size-keyword"] = "auto | min-content | max-content | fit-content | stretch",
            ["url-set"] = "<image-set()>",
            ["top"] = "auto | <length>", ["right"] = "auto | <length>", ["bottom"] = "auto | <length>", ["left"] = "auto | <length>",
            ["font-src-list"] = "<font-src>#",
            ["steps()"] = "steps( <integer [1,∞]> [ , <step-position> ]? )",
            ["baseline-position"] = "[ first | last ]? baseline",
            ["content-position"] = "center | start | end | flex-start | flex-end | flow-start | flow-end",
            ["scaleX()"] = "scaleX( <number-percentage> )",
            ["scaleY()"] = "scaleY( <number-percentage> )",
            ["text-edge"] = "[ text | ideographic | ideographic-ink | cap | ex ] [ text | ideographic | ideographic-ink | alphabetic ]? | alphabetic",
            ["symbols()"] = "symbols( <symbols-type>? <string>+ )",
            ["line-names"] = "[ '[' <custom-ident>* ']' ]+",
            ["shape-radius"] = "<length-percentage [0,∞]> | closest-side | farthest-side",
            ["circle()"] = "circle( <shape-radius>? [ at <position> ]? )",
            ["ellipse()"] = "ellipse( <shape-radius>{2}? [ at <position> ]? )",
        };

        private IEnumerable<GrammarMatch> MatchType(TypeNode node, int pos, int depth)
        {
            if (!node.IsProperty && node.Name == "calc-size()")
            {
                if (pos < _tokens.Count && _tokens[pos] is FunctionValue sizing
                    && CssMathParser.TryParseSizing(sizing, out CssValue? parsed) && parsed is CssSizingCalculationValue calculation
                    && calculation.FitsProperty(_scope == "width" && _property is "flex" or "flex-basis" ? "flex-basis" : _scope))
                    yield return Captured(node.Name, pos, Leaf(pos + 1, calculation));
                yield break;
            }
            if (!node.IsProperty && node.Name == "palette-mix()")
            {
                if (pos < _tokens.Count && _tokens[pos] is FunctionValue palette
                    && CssFontPaletteParser.TryParse(palette, out CssValue? parsed))
                    yield return Captured(node.Name, pos, Leaf(pos + 1, parsed!));
                yield break;
            }
            if (pos < _tokens.Count && !node.IsProperty && CssTypeParser.Primitive(node.Name, _tokens[pos], node.Min, node.Max, out CssValue? primitive,
                _tokens[pos] is FunctionValue && node.Name is "number" or "integer" or "percentage" ? PercentageContext() : null))
            {
                if (primitive != null && !ExcludedIdent(node.Name, primitive))
                    yield return Captured(node.Name, pos, Leaf(pos + 1, primitive));
                yield break;
            }
            if (!node.IsProperty && IsPrimitive(node.Name)) yield break;
            if (node.Name is "declaration-value" or "any-value")
            {
                for (int end = _tokens.Count; end > pos; end--)
                {
                    var span = new List<ComponentValue>();
                    bool valid = true;
                    for (int i = pos; i < end; i++)
                    {
                        valid &= CssPropertyParser.ValidCustomToken(_tokens[i]);
                        span.Add(_tokens[i]);
                    }
                    if (valid && (node.Name == "any-value" || CssPropertyParser.ValidDeclaration(span)))
                        yield return Captured(node.Name, pos, Leaf(end, new CssUnparsedValue(span)));
                }
                yield break;
            }
            if (node.Name == "unicode-range-token")
            {
                foreach (GrammarMatch match in UnicodeRange(pos)) yield return Captured(node.Name, pos, match);
                yield break;
            }
            GrammarMatcher matcher = node.IsProperty ? new GrammarMatcher(this, node.Name)
                : node.Name is "family-name" or "font-family-name" ? new GrammarMatcher(this, "font-family-name")
                : node.Name == "basic-shape" ? new GrammarMatcher(this, "basic-shape") : this;
            foreach (GrammarNode reference in Resolve(node))
                foreach (GrammarMatch match in matcher.Walk(reference, pos, depth + 1))
                {
                    if (ExcludedIdent(node.Name, match.Value)) continue;
                    GrammarMatch ordered = match;
                    if (node.Name == "try-tactic")
                    {
                        var tactics = new List<CssValue>(match.End - pos);
                        for (int i = pos; i < match.End; i++)
                            tactics.Add(new CssKeywordValue(NecronomiDom.Dom.Names.AsciiLower(((PreservedToken)_tokens[i]).Token.Value)));
                        ordered = match with { Value = Pack(tactics) };
                    }
                    yield return Captured(node.IsProperty ? "'" + node.Name + "'" : node.Name, pos, ordered);
                }
        }
        private static bool IsPrimitive(string type) => type is "color" or "image" or "length" or "length-percentage" or "angle" or "time" or "frequency"
            or "resolution" or "flex" or "number" or "number-percentage" or "opacity-value" or "integer" or "zero" or "percentage" or "ident" or "ident-token" or "custom-ident" or "dashed-ident"
            or "custom-property-name" or "string" or "string-token" or "url" or "url-token" or "hex-color" or "dimension" or "dimension-token" or "hash-token" or "number-token";


        private bool ExcludedIdent(string type, CssValue value)
        {
            if (value is not CssKeywordValue keyword) return false;
            if (type is "keyframes-name" or "counter-style-name" || type == "custom-ident" && _scope == "animation-name")
                return keyword.Is("none");
            if (type == "custom-ident" && _scope.StartsWith("grid-", StringComparison.Ordinal)) return keyword.Is("span") || keyword.Is("auto");
            if (type == "custom-ident" && _scope is "container-name" or "container") return keyword.Is("none") || keyword.Is("and") || keyword.Is("or") || keyword.Is("not");
            if (type == "custom-ident" && (_scope.StartsWith("counter-", StringComparison.Ordinal) || _scope is "transition-property" or "transition")) return keyword.Is("none");
            if (type == "custom-ident" && _scope is "font-family" or "font-family-name")
                return keyword.Is("serif") || keyword.Is("sans-serif") || keyword.Is("monospace") || keyword.Is("cursive") || keyword.Is("fantasy") || keyword.Is("system-ui")
                    || keyword.Is("ui-serif") || keyword.Is("ui-sans-serif") || keyword.Is("ui-monospace") || keyword.Is("ui-rounded") || keyword.Is("math") || keyword.Is("fangsong");
            return false;
        }

        private IEnumerable<GrammarNode> Resolve(TypeNode node)
        {
            if (node.IsProperty)
            {
                PropertyDefinition? property = CssPropertyRegistry.GetUnfiltered(node.Name);
                if (property != null) yield return property.Grammar;
                yield break;
            }
            if (_scope == "clip" && node.Name is "top" or "right" or "bottom" or "left")
            {
                yield return ParseCached("<length> | auto");
                yield break;
            }
            if (BrowserTypes.TryGetValue(node.Name, out string? replacement))
            {
                yield return ParseCached(replacement);
                yield break;
            }
            Dictionary<string, List<WebrefSyntax>> table = node.Name.EndsWith("()", StringComparison.Ordinal)
                ? WebrefData.Current.Functions : WebrefData.Current.Types;
            string name = node.Name.Trim('<', '>');
            if (!table.TryGetValue(name, out List<WebrefSyntax>? definitions)) yield break;
            bool scoped = false;
            foreach (WebrefSyntax definition in definitions)
                if (ContainsScope(definition.For, _scope)) scoped = true;
            foreach (WebrefSyntax definition in definitions)
                if (scoped ? ContainsScope(definition.For, _scope) : definition.For.Count == 0 || definitions.Count == 1)
                    yield return ParseCached(definition.Syntax);
        }

        private static bool ContainsScope(IReadOnlyList<string> scopes, string scope)
        {
            foreach (string candidate in scopes) if (candidate == scope || candidate == "<" + scope + ">") return true;
            return false;
        }
        private static GrammarNode ParseCached(string syntax) => ParsedSyntaxes.GetOrAdd(syntax, GrammarParser.Parse);
        private static GrammarMatch Captured(string name, int start, GrammarMatch match)
        {
            var captures = new List<GrammarCapture>(match.Captures) { new(name, start, match.End, match.Value) };
            return match with { Captures = captures };
        }

        private IEnumerable<GrammarMatch> UnicodeRange(int pos)
        {
            string text = "";
            for (int i = pos; i < _tokens.Count && i < pos + 8; i++)
            {
                text += CssTokenSerializer.Serialize(_tokens[i]);
                if (!text.StartsWith("u+", StringComparison.OrdinalIgnoreCase)) continue;
                string body = text[2..];
                string[] range = body.Split('-');
                if (range.Length > 2 || body.Length == 0) continue;
                bool wildcard = body.Contains('?');
                string lo = range[0].Replace('?', '0');
                string hi = (range.Length == 2 ? range[1] : range[0]).Replace('?', 'F');
                if (lo.Length is < 1 or > 6 || hi.Length is < 1 or > 6 || wildcard && range.Length != 1) continue;
                if (wildcard && body.TrimEnd('?').Contains('?')) continue;
                if (int.TryParse(lo, System.Globalization.NumberStyles.HexNumber, null, out int min)
                    && int.TryParse(hi, System.Globalization.NumberStyles.HexNumber, null, out int max)
                    && min <= max && max <= 0x10ffff)
                    yield return Leaf(i + 1, new CssDelimValue("U+" + min.ToString("X", System.Globalization.CultureInfo.InvariantCulture)
                        + (min == max ? "" : "-" + max.ToString("X", System.Globalization.CultureInfo.InvariantCulture))));
            }
        }
    }
}
