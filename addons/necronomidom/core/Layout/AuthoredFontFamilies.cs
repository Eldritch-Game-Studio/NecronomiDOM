using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Url;

namespace NecronomiDom.Layout;

/// <summary>Bounded regular-face admission. A family is authored or explicitly mapped, never a fallback search.</summary>
public static class AuthoredFontFamilies
{
    public static string Name(CssValue? value)
    {
        if (value is CssStringValue text && text.Value.Length > 0) return text.Value;
        if (value is CssKeywordValue keyword) return keyword.Value;
        if (value is CssListValue { Separator: CssListSeparator.Space } words)
        {
            var name = new StringBuilder();
            foreach (var item in words.Items)
            {
                if (item is not CssKeywordValue word) throw Refusal("font-family", value);
                if (name.Length > 0) name.Append(' ');
                name.Append(word.Value);
            }
            if (name.Length > 0) return name.ToString();
        }
        throw Refusal("font-family (one named family required; fallback lists are disabled)", value);
    }

    public static Dictionary<string, string> Resolve(Document document, IReadOnlyDictionary<string, string> mapped)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapped) result.Add(pair.Key, pair.Value);
        var authored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in document.StyleSheets) AddSheet(sheet);
        foreach (var sheet in document.AdoptedStyleSheets) AddSheet(sheet);
        return result;

        void AddSheet(CSSStyleSheet sheet)
        {
            if (sheet.Disabled) return;
            foreach (var rule in sheet.CssRules)
            {
                if (rule is CSSGroupingRule group) RejectNestedFaces(group.CssRules);
                if (rule is not CSSFontFaceRule face) continue;
                if (sheet.Media.MediaText.Length > 0 && sheet.Media.MediaText != "all")
                    throw new NotSupportedException("Runtime @font-face requires an unconditional stylesheet.");
                string family = Name(face.Style.GetSpecifiedValue("font-family"));
                foreach (var descriptor in face.Style.EnumerateLonghands())
                {
                    string value = face.Style.GetPropertyValue(descriptor.Name);
                    if (descriptor.Name is "font-family" or "src") continue;
                    if (descriptor.Name == "font-style" && value == "normal") continue;
                    if (descriptor.Name == "font-weight" && value is "normal" or "400") continue;
                    if (descriptor.Name == "font-display" && value is "auto" or "block") continue;
                    throw Refusal("@font-face " + descriptor.Name, descriptor.Value);
                }
                CssValue? source = face.Style.GetSpecifiedValue("src");
                while (source is CssListValue list && list.Items.Count == 1) source = list.Items[0];
                if (source is not CssUrlValue url) throw Refusal("@font-face src (one resource URL required)", source);
                var resolution = ResourceUrlPolicy.Default.Resolve(url.Url, sheet.BaseUrl ?? document.BaseUrl
                    ?? throw new NotSupportedException("Runtime @font-face requires a resource base URL."), ResourceKind.Font);
                if (!resolution.Allowed || resolution.ResourcePath == null)
                    throw new NotSupportedException("Runtime @font-face URL refused: " + resolution);
                if (!authored.Add(family)) throw new NotSupportedException("Runtime @font-face has multiple faces for family: " + family);
                result[family] = resolution.ResourcePath;
            }
        }
    }

    private static void RejectNestedFaces(CSSRuleList rules)
    {
        foreach (var rule in rules)
        {
            if (rule is CSSFontFaceRule) throw new NotSupportedException("Runtime @font-face must be unconditional and top-level.");
            if (rule is CSSGroupingRule group) RejectNestedFaces(group.CssRules);
        }
    }
    private static NotSupportedException Refusal(string property, CssValue? value) =>
        new("Runtime authored font profile rejects " + property + ": " + value);
}
