using System;
using System.Collections.Generic;

namespace NecronomiDom.Css.Values
{
    /// <summary>Layered shorthand serialization per CSSOM § 6.7.2, Backgrounds 3 and Animations 2.</summary>
    public static partial class ShorthandSerializer
    {
        private static IReadOnlyList<CssValue> Items(CssValue value) => value is CssListValue { Separator: CssListSeparator.Comma } list ? list.Items : new[] { value };
        private static string? PositionLayers(Func<string, CssValue?> get)
        {
            CssValue? xv = get("background-position-x"), yv = get("background-position-y");
            if (xv == null || yv == null) return null;
            IReadOnlyList<CssValue> xs = Items(xv), ys = Items(yv);
            if (xs.Count != ys.Count) return null;
            var layers = new List<string>();
            for (int i = 0; i < xs.Count; i++) layers.Add(xs[i].Serialize() + " " + ys[i].Serialize());
            return string.Join(", ", layers);
        }
        private static string? Layers(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            bool mask = definition.Name == "mask";
            string prefix = mask ? "mask-" : "background-";
            string[] names = mask ? new[] { "mask-image", "mask-position", "mask-size", "mask-repeat", "mask-origin", "mask-clip", "mask-composite", "mask-mode" }
                : new[] { "background-image", "background-position-x", "background-position-y", "background-size", "background-repeat", "background-attachment", "background-origin", "background-clip" };
            var lists = new Dictionary<string, IReadOnlyList<CssValue>>(StringComparer.Ordinal);
            int count = -1;
            foreach (string name in names)
            {
                CssValue? value = get(name);
                if (value == null) return null;
                IReadOnlyList<CssValue> items = Items(value);
                if (count >= 0 && items.Count != count) return null;
                count = items.Count;
                lists[name] = items;
            }
            var layers = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string position = mask ? lists["mask-position"][i].Serialize()
                    : lists["background-position-x"][i].Serialize() + " " + lists["background-position-y"][i].Serialize();
                var parts = new List<string>();
                string image = lists[prefix + "image"][i].Serialize(), size = lists[prefix + "size"][i].Serialize();
                if (image != "none") parts.Add(image);
                if (position != "0% 0%" || size != "auto") parts.Add(position + (size == "auto" ? "" : " / " + size));
                string repeat = lists[prefix + "repeat"][i].Serialize();
                if (repeat != "repeat") parts.Add(repeat);
                if (!mask && lists["background-attachment"][i].Serialize() is string attachment && attachment != "scroll") parts.Add(attachment);
                string origin = lists[prefix + "origin"][i].Serialize(), clip = lists[prefix + "clip"][i].Serialize();
                if (origin != (mask ? "border-box" : "padding-box") || clip != "border-box")
                    parts.Add(mask && origin == "border-box" && clip == "no-clip" ? clip : origin == clip ? origin : origin + " " + clip);
                if (mask)
                {
                    string composite = lists["mask-composite"][i].Serialize(), mode = lists["mask-mode"][i].Serialize();
                    if (composite != "add") parts.Add(composite);
                    if (mode != "match-source") parts.Add(mode);
                }
                else if (i == count - 1 && get("background-color") is CssValue color
                    && !ReferenceEquals(color, ShorthandExpansion.InitialBackgroundColor)
                    && color.Serialize() != ShorthandExpansion.Initial("background-color")?.Serialize()) parts.Add(color.Serialize());
                layers.Add(parts.Count == 0 ? "none" : string.Join(" ", parts));
            }
            return string.Join(", ", layers);
        }

        private static string? Motion(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            var lists = new List<IReadOnlyList<CssValue>>();
            int count = -1;
            foreach (string name in definition.Longhands)
            {
                CssValue? value = get(name);
                if (value == null) return null;
                IReadOnlyList<CssValue> items = Items(value);
                if (count >= 0 && items.Count != count) return null;
                count = items.Count;
                lists.Add(items);
            }
            var layers = new List<string>();
            int[] order = definition.Name == "animation" ? new[] { 1, 2, 3, 4, 5, 6, 7, 0, 8 }
                : definition.Name == "transition" ? new[] { 0, 1, 2, 3, 4 } : new[] { 0, 1 };
            for (int i = 0; i < count; i++)
            {
                var parts = new List<string>();
                if (definition.Name == "animation-range")
                {
                    layers.Add(CssTimelineRangeValues.Serialize(lists[0][i], lists[1][i]));
                    continue;
                }
                foreach (int j in order)
                {
                    string name = definition.Longhands[j], text = lists[j][i].Serialize();
                    string? initial = ShorthandExpansion.Initial(name)?.Serialize();
                    bool durationRequired = name.EndsWith("-duration", StringComparison.Ordinal)
                        && lists[3][i].Serialize() != "0s";
                    if (text == initial && !durationRequired) continue;
                    parts.Add(text);
                }
                layers.Add(parts.Count == 0 ? definition.Name == "transition" ? "all" : definition.Name == "animation" ? "none" : "normal" : string.Join(" ", parts));
            }
            return string.Join(", ", layers);
        }
    }
}
