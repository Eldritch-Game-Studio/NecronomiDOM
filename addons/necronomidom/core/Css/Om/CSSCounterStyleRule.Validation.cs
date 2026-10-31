using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    public sealed partial class CSSCounterStyleRule
    {
        private static readonly HashSet<string> Predefined = new HashSet<string>(("decimal decimal-leading-zero arabic-indic armenian upper-armenian lower-armenian bengali cambodian khmer cjk-decimal devanagari georgian gujarati gurmukhi hebrew kannada lao malayalam mongolian myanmar oriya persian lower-roman upper-roman tamil telugu thai tibetan lower-alpha lower-latin upper-alpha upper-latin lower-greek hiragana hiragana-iroha katakana katakana-iroha disc circle square disclosure-open disclosure-closed cjk-earthly-branch cjk-heavenly-stem japanese-informal japanese-formal korean-hangul-formal korean-hanja-informal korean-hanja-formal simp-chinese-informal simp-chinese-formal trad-chinese-informal trad-chinese-formal cjk-ideographic ethiopic-numeric").Split(' '), StringComparer.Ordinal);
        internal static bool NonOverridable(string name) => Dom.Names.AsciiLower(name) is "none" or "decimal" or "disc" or "circle" or "square" or "disclosure-open" or "disclosure-closed";
        private static string CanonicalName(string name)
        { var lower = Dom.Names.AsciiLower(name); return Predefined.Contains(lower) ? lower : name; }
        /// <summary>Counter Styles 3 § 3.1 semantic validity, independent of the CSSOM rule's syntactic existence.</summary>
        public bool IsValidCounterStyle => ValidWith(null, null);
        private void SetDescriptor(string name, string source)
        {
            var tokens = CssParser.ParseComponentValueList(source);
            if (!CssDescriptorParser.TryParse("counter-style", name, tokens, out var value) || value == null) return;
            if (name == "system" && Algorithm(value) != Algorithm(Descriptor("system", null, null))) return;
            if (!ValidWith(name, value)) return;
            Style.SetProperty(name, source);
        }
        private CssValue? Descriptor(string name, string? replacedName, CssValue? replacement)
        {
            if (name == replacedName) return replacement;
            foreach (var declaration in Style.Declarations) if (declaration.Name == name) return declaration.Value;
            return null;
        }
        private bool ValidWith(string? name, CssValue? value)
        {
            var algorithm = Algorithm(Descriptor("system", name, value));
            var symbols = Descriptor("symbols", name, value);
            var additive = Descriptor("additive-symbols", name, value);
            if (algorithm == "extends") return symbols == null && additive == null;
            if (algorithm == "additive") return Count(additive) >= 1;
            return Count(symbols) >= (algorithm is "numeric" or "alphabetic" ? 2 : 1);
        }
        private static int Count(CssValue? value) => value == null ? 0 : value is CssListValue list ? list.Items.Count : 1;
        private static string Algorithm(CssValue? value)
        {
            if (value is CssListValue list && list.Items.Count > 0) value = list.Items[0];
            return value is CssKeywordValue keyword ? keyword.Value : "symbolic";
        }
    }
}
