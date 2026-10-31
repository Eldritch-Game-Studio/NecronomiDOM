using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Om
{
    internal static partial class CSSRuleParser
    {
        private static string? LayerName(IReadOnlyList<ComponentValue> source)
        {
            var values = CssParser.Trim(source);
            var result = new StringBuilder();
            var ident = true;
            foreach (var value in values)
            {
                if (CssParser.Is(value, CssTokenKind.Whitespace)) continue;
                if (ident)
                {
                    if (value is not PreservedToken token || token.Token.Kind != CssTokenKind.Ident || Reserved(token.Token.Value)) return null;
                    result.Append(CssSerialization.SerializeIdentifier(token.Token.Value));
                }
                else { if (!CssParser.IsDelim(value, '.')) return null; result.Append('.'); }
                ident = !ident;
            }
            return !ident ? result.ToString() : null;
        }
        private static IReadOnlyList<string>? LayerNames(IReadOnlyList<ComponentValue> values)
        {
            var names = new List<string>();
            foreach (var entry in CssParser.SplitComma(values)) { var name = LayerName(entry); if (name == null) return null; names.Add(name); }
            return names;
        }
        private static string? Url(ComponentValue value)
        {
            if (value is PreservedToken token && token.Token.Kind is CssTokenKind.String or CssTokenKind.Url) return token.Token.Value;
            if (value is FunctionValue function && Dom.Names.AsciiEqual(function.Name, "url"))
            {
                var args = CssParser.Trim(function.Arguments);
                if (args.Count == 1 && args[0] is PreservedToken text && text.Token.Kind == CssTokenKind.String) return text.Token.Value;
            }
            return null;
        }
        private static CSSNamespaceRule? Namespace(IReadOnlyList<ComponentValue> values)
        {
            if (values.Count == 0) return null;
            var p = 0;
            var prefix = "";
            if (values[p] is PreservedToken token && token.Token.Kind == CssTokenKind.Ident)
            {
                prefix = token.Token.Value;
                p++;
                Skip(values, ref p);
            }
            if (p >= values.Count || Url(values[p++]) is not string uri) return null;
            Skip(values, ref p);
            return p == values.Count ? new CSSNamespaceRule(prefix, uri) : null;
        }
        private static CSSImportRule? Import(IReadOnlyList<ComponentValue> values, string? source)
        {
            if (values.Count == 0 || Url(values[0]) is not string href) return null;
            var p = 1;
            Skip(values, ref p);
            string? layer = null;
            QueryCondition? supports = null;
            if (p < values.Count && CssParser.IsIdent(values[p], "layer")) { layer = ""; p++; Skip(values, ref p); }
            else if (p < values.Count && values[p] is FunctionValue layerFunction && Dom.Names.AsciiEqual(layerFunction.Name, "layer"))
            { layer = LayerName(layerFunction.Arguments); if (layer == null) return null; p++; Skip(values, ref p); }
            if (p < values.Count && values[p] is FunctionValue supportsFunction && Dom.Names.AsciiEqual(supportsFunction.Name, "supports"))
            {
                supports = SupportsParser.Parse(supportsFunction.Arguments, new Selectors.SelectorParseOptions { SourceText = source }) ?? SupportsParser.Parse("(" + CssTokenSerializer.Serialize(supportsFunction.Arguments) + ")");
                if (supports == null) return null;
                p++;
                Skip(values, ref p);
            }
            return new CSSImportRule(href, layer, supports, MediaQueryParser.Parse(CssParser.Slice(values, p, values.Count - p)));
        }
        private static CSSCustomMediaRule? CustomMedia(IReadOnlyList<ComponentValue> values)
        {
            if (values.Count < 2 || values[0] is not PreservedToken token || token.Token.Kind != CssTokenKind.Ident || !token.Token.Value.StartsWith("--", StringComparison.Ordinal) || token.Token.Value.Length == 2) return null;
            var query = CssParser.Trim(CssParser.Slice(values, 1, values.Count - 1));
            if (query.Count == 0) return null;
            if (query.Count == 1 && (CssParser.IsIdent(query[0], "true") || CssParser.IsIdent(query[0], "false"))) return new CSSCustomMediaRule(token.Token.Value, null, CssParser.IsIdent(query[0], "true"));
            return new CSSCustomMediaRule(token.Token.Value, MediaQueryParser.Parse(query));
        }
        private static void Skip(IReadOnlyList<ComponentValue> values, ref int p) { while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++; }
    }
}
