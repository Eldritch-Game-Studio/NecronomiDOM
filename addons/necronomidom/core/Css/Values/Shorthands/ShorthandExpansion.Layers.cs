using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Css.Values
{
    internal static partial class ShorthandExpansion
    {
        // CSSOM serialization of the omitted background color uses the constructed initial, not named-token provenance.
        internal static readonly CssValue InitialBackgroundColor = new CssAbsoluteColorValue(new CssResolvedColor("srgb", 0, 0, 0, 0), keyword: null, legacy: true);
        private static bool Layers(PropertyDefinition definition, IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            bool mask = definition.Name == "mask";
            string prefix = mask ? "mask-" : "background-";
            if (!mask) values["background-color"] = InitialBackgroundColor;
            var lists = new Dictionary<string, List<CssValue>>(StringComparer.Ordinal);
            foreach (string leaf in Leaves(definition))
                if (leaf.StartsWith(prefix, StringComparison.Ordinal) && leaf != "background-color" && !leaf.Contains("border", StringComparison.Ordinal)
                    && leaf != "background-blend-mode") lists[leaf] = new List<CssValue>();
            List<List<ComponentValue>> layers = Split(tokens, true);
            for (int i = 0; i < layers.Count; i++)
            {
                string type = mask ? "mask-layer" : i == layers.Count - 1 ? "final-bg-layer" : "bg-layer";
                GrammarMatch? match = new GrammarMatcher(layers[i], definition.Name).Match(GrammarParser.Parse("<" + type + ">"));
                if (match == null) return false;
                var layer = new Dictionary<string, CssValue>(StringComparer.Ordinal);
                foreach (var list in lists) layer[list.Key] = Initial(list.Key)!;
                CopyCapture(layer, prefix + "image", match, mask ? "mask-reference" : "bg-image");
                CopyCapture(layer, prefix + "size", match, "bg-size");
                CopyCapture(layer, prefix + "repeat", match, "repeat-style");
                if (mask)
                {
                    CopyCapture(layer, "mask-composite", match, "compositing-operator");
                    CopyCapture(layer, "mask-mode", match, "masking-mode");
                }
                else CopyCapture(layer, "background-attachment", match, "attachment");
                CssValue? position = Capture(match, mask ? "position" : "bg-position");
                if (position != null)
                {
                    if (!Position(CssParser.ParseComponentValueList(position.Serialize()), out CssValue x, out CssValue y)) return false;
                    if (mask) layer["mask-position"] = List(new[] { x, y });
                    else { layer["background-position-x"] = x; layer["background-position-y"] = y; }
                }
                var boxes = Captures(match, mask ? "geometry-box" : "visual-box");
                if (boxes.Count > 0)
                {
                    layer[prefix + "origin"] = boxes[0];
                    layer[prefix + "clip"] = boxes.Count > 1 ? boxes[1] : boxes[0];
                }
                CssValue? clip = mask ? null : Capture(match, "bg-clip");
                if (clip != null) layer[prefix + "clip"] = clip;
                if (!mask && boxes.Count == 0 && ContainsIdent(layers[i], "border-area")) layer["background-origin"] = Keyword("border-box");
                if (ContainsIdent(layers[i], "no-clip")) layer["mask-clip"] = Keyword("no-clip");
                CssValue? color = Capture(match, "'background-color'");
                if (color != null) values["background-color"] = color;
                foreach (var entry in lists) entry.Value.Add(layer[entry.Key]);
            }
            foreach (var entry in lists) values[entry.Key] = List(entry.Value, CssListSeparator.Comma);
            return true;
        }
        private static void CopyCapture(Dictionary<string, CssValue> values, string target, GrammarMatch match, string type)
        {
            CssValue? value = Capture(match, type);
            if (value != null) values[target] = CssSpecifiedValueNormalizer.Normalize(target, value);
        }
        private static List<CssValue> Captures(GrammarMatch match, string name)
        {
            var found = new List<GrammarCapture>();
            foreach (GrammarCapture capture in match.Captures) if (capture.Name == name) found.Add(capture);
            found.Sort((a, b) => a.Start.CompareTo(b.Start));
            var result = new List<CssValue>(found.Count);
            foreach (GrammarCapture capture in found) result.Add(capture.Value);
            return result;
        }

        private static bool Motion(PropertyDefinition definition, IReadOnlyList<ComponentValue> tokens, Dictionary<string, CssValue> values)
        {
            bool animation = definition.Name == "animation";
            var lists = new Dictionary<string, List<CssValue>>(StringComparer.Ordinal);
            foreach (string leaf in definition.Longhands) if (CssPropertyRegistry.Get(leaf) != null) lists[leaf] = new List<CssValue>();
            foreach (List<ComponentValue> layer in Split(tokens, true))
            {
                GrammarMatch? match = new GrammarMatcher(layer, definition.Name).Match(GrammarParser.Parse(animation ? "<single-animation>" : "<single-transition>"));
                if (match == null) return false;
                var current = new Dictionary<string, CssValue>(StringComparer.Ordinal);
                foreach (var entry in lists) current[entry.Key] = Initial(entry.Key)!;
                if (animation)
                {
                    CopyCapture(current, "animation-duration", match, "'animation-duration'");
                    var times = Captures(match, "time");
                    if (times.Count > 1) current["animation-delay"] = times[1];
                    foreach (string suffix in new[] { "iteration-count", "direction", "fill-mode", "play-state", "timeline" })
                        CopyCapture(current, "animation-" + suffix, match, "single-animation-" + suffix);
                    CopyCapture(current, "animation-name", match, "keyframes-name");
                    if (current["animation-name"].Serialize() == "none")
                    {
                        foreach (ComponentValue component in layer)
                            if (component is PreservedToken p && p.Token.Kind == CssTokenKind.String) current["animation-name"] = new CssStringValue(p.Token.Value);
                    }
                    CopyCapture(current, "animation-timing-function", match, "easing-function");
                }
                else
                {
                    CopyCapture(current, "transition-property", match, "single-transition-property");
                    if (ContainsIdent(layer, "none")) current["transition-property"] = Keyword("none");
                    var times = Captures(match, "time");
                    if (times.Count > 0) current["transition-duration"] = times[0];
                    if (times.Count > 1) current["transition-delay"] = times[1];
                    CopyCapture(current, "transition-timing-function", match, "easing-function");
                    CopyCapture(current, "transition-behavior", match, "transition-behavior-value");
                }
                foreach (var entry in lists) entry.Value.Add(current[entry.Key]);
            }
            foreach (var entry in lists) values[entry.Key] = List(entry.Value, CssListSeparator.Comma);
            return true;
        }
    }
}
