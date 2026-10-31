using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    /// <summary>CSSOM § 6.7.2 shorthand serialization, including CSS-wide and pending-substitution constraints.</summary>
    public static partial class ShorthandSerializer
    {
        public static bool TrySerialize(string shorthand, Func<string, CssValue?> longhand, out string value)
        {
            value = "";
            PropertyDefinition? definition = CssPropertyRegistry.Get(shorthand);
            if (definition == null || !definition.IsShorthand) return false;
            var leaves = new Dictionary<string, CssValue>(StringComparer.Ordinal);
            foreach (string name in ShorthandExpansion.Leaves(definition))
            {
                CssValue? item = longhand(name);
                if (item == null) return false;
                leaves.Add(name, item);
            }
            string? special = null;
            bool hasSpecial = false;
            foreach (CssValue item in leaves.Values)
            {
                string? current = item switch
                {
                    CssWideKeyword wide => wide.Serialize(),
                    CssPendingSubstitutionValue pending when pending.Shorthand == definition.Name => pending.SourceText,
                    CssSystemFontValue system when definition.Name == "font" => system.SystemFont,
                    _ => null,
                };
                if (current != null)
                {
                    if (hasSpecial && special != current) return false;
                    special = current; hasSpecial = true;
                }
                else if (item is CssPendingSubstitutionValue) return false;
            }
            if (hasSpecial)
            {
                foreach (CssValue item in leaves.Values)
                    if (item is not (CssWideKeyword or CssPendingSubstitutionValue or CssSystemFontValue)) return false;
                value = special!;
                return true;
            }
            foreach (string reset in definition.ResetLonghands)
            {
                PropertyDefinition? resetDefinition = CssPropertyRegistry.Get(reset);
                if (resetDefinition == null) continue;
                IEnumerable<string> names = resetDefinition.IsShorthand ? ShorthandExpansion.Leaves(resetDefinition) : new[] { resetDefinition.Name };
                foreach (string name in names)
                {
                    CssValue? initial = ShorthandExpansion.Initial(name);
                    if (initial == null || !leaves.TryGetValue(name, out CssValue? item) || item.Serialize() != initial.Serialize()) return false;
                }
            }
            Func<string, CssValue?> get = name => leaves.TryGetValue(name, out CssValue? item) ? item : null;
            string? text = Compose(definition, get);
            if (text == null || !Equivalent(definition.Name, text, leaves)) return false;
            value = text;
            return true;
        }

        private static bool Equivalent(string shorthand, string text, Dictionary<string, CssValue> expected)
        {
            ParsedProperty? parsed = CssPropertyParser.Parse(shorthand, CssParser.ParseComponentValueList(text));
            if (parsed == null || parsed.Longhands.Count != expected.Count) return false;
            foreach (var item in parsed.Longhands)
            {
                if (!expected.TryGetValue(item.Longhand, out CssValue? value)) return false;
                if (item.Value.Serialize() == value.Serialize()) continue;
                if (shorthand == "vertical-align" && item.Longhand == "baseline-shift"
                    && ZeroShift(value) && ZeroShift(item.Value)) continue;
                if (shorthand == "font" && item.Longhand is "font-width" or "font-stretch"
                    && CssFontWidthValues.Equivalent(value, item.Value)) continue;
                if (shorthand == "font" && item.Longhand == "font-weight" && value is CssNumberValue { Value: 400 }
                    && item.Value is CssKeywordValue { Value: "normal" }) continue;
                if (shorthand != "animation-range" || item.Longhand != "animation-range-end"
                    || !expected.TryGetValue("animation-range-start", out CssValue? start)
                    || !CssTimelineRangeValues.EquivalentEnd(start, value, item.Value)) return false;
            }
            return true;
        }
        private static string? Child(string name, Func<string, CssValue?> get)
        {
            CssValue? direct = get(CssPropertyRegistry.Get(name)?.Name ?? name);
            if (direct != null) return direct.Serialize();
            return TrySerialize(name, get, out string text) ? text : null;
        }
        private static string? Compose(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            string name = definition.Name;
            if (name == "border-radius" || name.EndsWith("-radius", StringComparison.Ordinal)) return Radius(definition, get);
            if (name is "margin" or "padding" or "inset" or "scroll-margin" or "scroll-padding" or "border-color" or "border-style" or "border-width"
                || name.StartsWith("margin-", StringComparison.Ordinal) || name.StartsWith("padding-", StringComparison.Ordinal)
                || name.StartsWith("inset-", StringComparison.Ordinal) || name.StartsWith("scroll-margin-", StringComparison.Ordinal)
                || name.StartsWith("scroll-padding-", StringComparison.Ordinal) || name is "overflow" or "overscroll-behavior" or "gap"
                or "border-block-color" or "border-block-width" or "border-block-style" or "border-inline-color" or "border-inline-width" or "border-inline-style")
                return Box(definition, get);
            return name switch
            {
                "background-position" => PositionLayers(get),
                "background" or "mask" => Layers(definition, get),
                "animation" or "transition" or "animation-range" => Motion(definition, get),
                "grid" or "grid-template" => Grid(definition, get),
                "grid-row" or "grid-column" or "grid-area" => GridLines(definition, get),
                "font" => Font(definition, get),
                "font-synthesis" => FontSynthesis(get),
                "font-variant" => FontVariant(definition, get),
                "white-space" => WhiteSpace(get),
                "text-box" => TextBox(get),
                "vertical-align" => VerticalAlign(get),
                "outline" => Outline(get),
                "list-style" => ListStyle(get),
                "text-decoration-skip" => Child("text-decoration-skip-ink", get),
                "border-block" or "border-inline" => EqualChildren(definition, get),
                "border-image" or "mask-border" => BorderImage(definition, get),
                "container" => Child("container-name", get) + (Child("container-type", get) == "normal" ? "" : " / " + Child("container-type", get)),
                "position-try" => Child("position-try-order", get) == "normal" ? Child("position-try-fallbacks", get)
                    : Child("position-try-order", get) + " " + Child("position-try-fallbacks", get),
                "flex" => JoinChildren(definition, get, " "),
                "place-content" or "place-items" or "place-self" or "contain-intrinsic-size" => Pair(definition, get),
                _ => Minimal(definition, get),
            };
        }
        private static string? JoinChildren(PropertyDefinition definition, Func<string, CssValue?> get, string separator)
        {
            var items = new List<string>();
            foreach (string name in definition.Longhands)
            {
                string? value = Child(name, get);
                if (value == null) return null;
                items.Add(value);
            }
            return string.Join(separator, items);
        }
        private static string? EqualChildren(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            string? first = null;
            foreach (string name in definition.Longhands)
            {
                string? value = Child(name, get);
                if (value == null || first != null && first != value) return null;
                first = value;
            }
            return first;
        }
        private static string? Minimal(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            var parts = new List<string>();
            foreach (string name in definition.Longhands)
            {
                string? text = Child(name, get);
                if (text == null) return null;
                string? initial = ShorthandExpansion.Initial(name)?.Serialize();
                if (text != initial) parts.Add(text);
            }
            if (parts.Count == 0)
            {
                if (definition.Name == "text-decoration") return "none";
                return Child(definition.Longhands[0], get);
            }
            return string.Join(" ", parts);
        }
        private static string? Pair(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            string? first = Child(definition.Longhands[0], get), second = Child(definition.Longhands[1], get);
            if (first == null || second == null) return null;
            return first == second ? first : first + " " + second;
        }
    }
}
