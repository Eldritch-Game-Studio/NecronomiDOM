using System;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorMatcher
    {
        private static bool MatchesLanguage(Element element, PseudoClassSelector pseudo)
        {
            string language = "";
            for (Element? current = element; current != null; current = current.ParentElement ?? (current.ParentNode as ShadowRoot)?.Host)
            {
                string? value = current.GetAttributeNS("http://www.w3.org/XML/1998/namespace", "lang") ?? current.GetAttribute("lang");
                if (value == null) continue;
                language = value;
                break;
            }
            if (pseudo.LanguageRanges is { } ranges)
            {
                for (int i = 0; i < ranges.Count; i++) if (ExtendedLanguageMatch(language.AsSpan(), ranges[i].AsSpan())) return true;
                return false;
            }
            return ExtendedLanguageMatch(language.AsSpan(), (pseudo.Argument ?? "").AsSpan().Trim().Trim('"').Trim('\''));
        }
        /// <summary>RFC 4647 § 3.3.2: subtags may be skipped, but singleton extension boundaries cannot.</summary>
        private static bool ExtendedLanguageMatch(ReadOnlySpan<char> language, ReadOnlySpan<char> range)
        {
            if (range.IsEmpty) return language.IsEmpty;
            if (language.IsEmpty) return false;
            int languagePosition = 0, rangePosition = 0;
            var first = Subtag(range, ref rangePosition);
            var actual = Subtag(language, ref languagePosition);
            if (!first.SequenceEqual("*") && !Names.AsciiEqual(first, actual)) return false;
            while (rangePosition <= range.Length)
            {
                var wanted = Subtag(range, ref rangePosition);
                if (wanted.SequenceEqual("*")) continue;
                bool matched = false;
                while (languagePosition <= language.Length)
                {
                    actual = Subtag(language, ref languagePosition);
                    if (Names.AsciiEqual(wanted, actual)) { matched = true; break; }
                    if (actual.Length == 1) return false;
                }
                if (!matched) return false;
            }
            return true;
        }
        private static ReadOnlySpan<char> Subtag(ReadOnlySpan<char> value, ref int position)
        {
            int start = position;
            int separator = value[start..].IndexOf('-');
            int end = separator < 0 ? value.Length : start + separator;
            position = end + 1;
            return value[start..end];
        }
    }
}
