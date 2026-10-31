using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;

namespace NecronomiDom.Css.Values
{
    /// <summary>A system-font longhand whose platform-dependent value is resolved at computed time (CSS Fonts 4 § 10).</summary>
    public sealed class CssSystemFontValue : CssValue
    {
        public CssSystemFontValue(string systemFont, string longhand) { SystemFont = systemFont; Longhand = longhand; }
        public string SystemFont { get; }
        public string Longhand { get; }
        public override void AppendTo(StringBuilder sb) { }
    }

    internal static partial class ShorthandExpansion
    {
        private static bool Font(IReadOnlyList<ComponentValue> tokens, GrammarMatch match, Dictionary<string, CssValue> values)
        {
            if (tokens.Count == 1 && Capture(match, "system-font-family-name") is CssKeywordValue system)
            {
                PropertyDefinition definition = CssPropertyRegistry.Get("font")!;
                foreach (string leaf in Leaves(definition)) values[leaf] = new CssSystemFontValue(system.Value, leaf);
                return true;
            }
            foreach (string name in new[] { "font-style", "font-weight", "font-size", "line-height" })
            {
                CssValue? capture = Capture(match, "'" + name + "'");
                if (capture != null && !Put(values, name, capture)) return false;
            }
            CssValue? variant = Capture(match, "font-variant-css2");
            if (variant != null && !Put(values, "font-variant", variant)) return false;
            CssValue? width = Capture(match, "font-width-css3");
            if (width != null && !Put(values, "font-stretch", width)) return false;
            var families = new List<CssValue>();
            foreach (GrammarCapture capture in match.Captures)
                if (capture.Name == "'font-family'") families.Add(capture.Value);
            return families.Count != 0 && Put(values, "font-family", List(families, CssListSeparator.Comma));
        }

        private static bool FontSynthesis(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            foreach (string name in new[] { "font-synthesis-weight", "font-synthesis-style", "font-synthesis-small-caps", "font-synthesis-position" })
                if (CssPropertyRegistry.Get(name) != null) values[name] = Keyword("none");
            foreach (ComponentValue token in tokens)
            {
                if (token is not PreservedToken p) return false;
                if (p.Token.IsIdent("oblique-only")) { values["font-synthesis-style"] = Keyword("oblique-only"); continue; }
                string name = "font-synthesis-" + NecronomiDom.Dom.Names.AsciiLower(p.Token.Value);
                if (values.ContainsKey(name)) values[name] = Keyword("auto");
            }
            return true;
        }

        private static bool TextBox(GrammarMatch match, Dictionary<string, CssValue> values)
        {
            CssValue? trim = Capture(match, "'text-box-trim'"), edge = Capture(match, "'text-box-edge'");
            if (trim == null && edge == null) return true;
            values["text-box-trim"] = trim ?? Keyword("trim-both");
            if (edge != null) values["text-box-edge"] = CssSpecifiedValueNormalizer.Normalize("text-box-edge", edge);
            return true;
        }

        private static bool FontVariant(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            if (tokens.Count == 1 && tokens[0] is PreservedToken p && (p.Token.IsIdent("normal") || p.Token.IsIdent("none")))
            {
                if (p.Token.IsIdent("none")) values["font-variant-ligatures"] = Keyword("none");
                return true;
            }
            var groups = new Dictionary<string, List<ComponentValue>>(StringComparer.Ordinal);
            foreach (string leaf in Leaves(CssPropertyRegistry.Get("font-variant")!)) groups[leaf] = new List<ComponentValue>();
            foreach (ComponentValue token in tokens)
            {
                bool found = false;
                foreach (var group in groups)
                {
                    if (CssPropertyParser.Parse(group.Key, new[] { token }) == null) continue;
                    group.Value.Add(token);
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            foreach (var group in groups)
                if (group.Value.Count != 0 && !ParsePut(values, group.Key, group.Value)) return false;
            return true;
        }

        private static bool WhiteSpace(IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            if (tokens.Count == 1 && tokens[0] is PreservedToken p)
            {
                string name = NecronomiDom.Dom.Names.AsciiLower(p.Token.Value);
                if (name is "normal" or "pre" or "pre-wrap" or "pre-line")
                {
                    values["white-space-collapse"] = Keyword(name == "normal" ? "collapse" : name == "pre-line" ? "preserve-breaks" : "preserve");
                    values["text-wrap-mode"] = Keyword(name == "pre" ? "nowrap" : "wrap");
                    return true;
                }
            }
            foreach (ComponentValue token in tokens)
            {
                bool found = false;
                foreach (string leaf in Leaves(CssPropertyRegistry.Get("white-space")!))
                {
                    if (CssPropertyParser.Parse(leaf, new[] { token }) == null) continue;
                    if (!ParsePut(values, leaf, new[] { token })) return false;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }
    }
}
