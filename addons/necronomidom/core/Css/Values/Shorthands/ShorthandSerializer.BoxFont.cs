using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.Values
{
    /// <summary>Box and font shorthand serialization per CSSOM § 6.7.2, Backgrounds 3 and Fonts 4.</summary>
    public static partial class ShorthandSerializer
    {
        private static string? Box(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            var parts = new List<string>();
            foreach (string name in definition.Longhands)
            {
                string? value = Child(name, get);
                if (value == null) return null;
                parts.Add(value);
            }
            return Compress(parts);
        }
        private static string Compress(IReadOnlyList<string> parts)
        {
            int count = parts.Count;
            if (count == 4 && parts[3] == parts[1]) count--;
            if (count == 3 && parts[2] == parts[0]) count--;
            if (count == 2 && parts[1] == parts[0]) count--;
            var result = new List<string>(count);
            for (int i = 0; i < count; i++) result.Add(parts[i]);
            return string.Join(" ", result);
        }
        private static string? Radius(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            var horizontal = new List<string>();
            var vertical = new List<string>();
            foreach (string name in definition.Longhands)
            {
                CssValue? value = get(name);
                if (value == null) return null;
                if (value is CssListValue { Separator: CssListSeparator.Space } list)
                {
                    if (list.Items.Count != 2) return null;
                    horizontal.Add(list.Items[0].Serialize()); vertical.Add(list.Items[1].Serialize());
                }
                else { horizontal.Add(value.Serialize()); vertical.Add(value.Serialize()); }
            }
            string h = Compress(horizontal), v = Compress(vertical);
            return h == v ? h : h + " / " + v;
        }
        private static string? BorderImage(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            string prefix = definition.Name + "-";
            string? source = Child(prefix + "source", get), slice = Child(prefix + "slice", get), width = Child(prefix + "width", get), outset = Child(prefix + "outset", get), repeat = Child(prefix + "repeat", get);
            if (source == null || slice == null || width == null || outset == null || repeat == null) return null;
            string text = source + " " + slice + " / " + width + " / " + outset + " " + repeat;
            if (definition.Name == "mask-border" && Child("mask-border-mode", get) is string mode && mode != "alpha") text += " " + mode;
            return text;
        }
        private static string? Font(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            var prefix = new List<string>();
            foreach (string name in new[] { "font-style", "font-variant", "font-weight", "font-stretch" })
            {
                string? value = Child(name, get);
                if (value == null) return null;
                if (name == "font-stretch" && get(CssPropertyRegistry.Get(name)?.Name ?? name) is CssPercentageValue width)
                {
                    value = CssFontWidthValues.Keyword(width.Value);
                    if (value == null) return null;
                }
                if (name == "font-weight" && get(name) is CssNumberValue { Value: 400 }) value = "normal";
                if (value != "normal") prefix.Add(value);
            }
            string? size = Child("font-size", get), height = Child("line-height", get), family = Child("font-family", get);
            if (size == null || height == null || family == null) return null;
            prefix.Add(size + (height == "normal" ? "" : " / " + height));
            prefix.Add(family);
            return string.Join(" ", prefix);
        }
        private static string? FontVariant(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            var parts = new List<string>();
            foreach (string name in definition.Longhands)
            {
                string? value = Child(name, get);
                if (value == null) return null;
                if (value != "normal") parts.Add(value);
            }
            return parts.Count == 0 ? "normal" : string.Join(" ", parts);
        }
        private static string? FontSynthesis(Func<string, CssValue?> get)
        {
            var parts = new List<string>();
            foreach (string suffix in new[] { "weight", "style", "small-caps", "position" })
            {
                CssValue? value = get("font-synthesis-" + suffix);
                if (value == null && CssPropertyRegistry.Get("font-synthesis-" + suffix) == null) continue;
                if (suffix == "style" && value is CssKeywordValue oblique && oblique.Is("oblique-only")) { parts.Add("oblique-only"); continue; }
                if (value is not CssKeywordValue k || !k.Is("auto") && !k.Is("none")) return null;
                if (k.Is("auto")) parts.Add(suffix);
            }
            return parts.Count == 0 ? "none" : string.Join(" ", parts);
        }
        private static string? TextBox(Func<string, CssValue?> get)
        {
            string? trim = Child("text-box-trim", get), edge = Child("text-box-edge", get);
            if (trim == null || edge == null) return null;
            if (edge == "auto") return trim == "none" ? "normal" : trim;
            return trim == "trim-both" ? edge : trim + " " + edge;
        }
        private static string? ListStyle(Func<string, CssValue?> get)
        {
            string? position = Child("list-style-position", get), image = Child("list-style-image", get), type = Child("list-style-type", get);
            if (position == null || image == null || type == null) return null;
            var parts = new List<string>();
            if (position != "outside" || type is "inside" or "outside") parts.Add(position);
            if (image != "none") parts.Add(image);
            if (type != "disc") parts.Add(type);
            return parts.Count == 0 ? "outside" : string.Join(" ", parts);
        }

        private static string? WhiteSpace(Func<string, CssValue?> get)
        {
            string? collapse = Child("white-space-collapse", get), wrap = Child("text-wrap-mode", get);
            if (collapse == null || wrap == null) return null;
            return (collapse, wrap) switch
            {
                ("collapse", "wrap") => "normal", ("collapse", "nowrap") => "nowrap", ("preserve", "nowrap") => "pre", ("preserve", "wrap") => "pre-wrap", ("preserve-breaks", "wrap") => "pre-line",
                (_, "wrap") => collapse, _ => collapse + " " + wrap,
            };
        }
    }
}
