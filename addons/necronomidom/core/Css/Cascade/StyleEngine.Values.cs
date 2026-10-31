using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    public sealed partial class StyleEngine
    {
        private ComputedValueContext Context(Entry entry, Entry? parent, Dictionary<string, CssValue> specified)
        {
            if (ValueContext != null || ContainerEnvironment != null || !CanObserveMedia) _computedViewportDependent = true;
            ComputedValueContext supplied = ValueContext?.Invoke(entry.Element) ?? default;
            bool isRoot = ReferenceEquals(entry.Element, _document.DocumentElement) && entry.Pseudo == null;
            Entry? root = isRoot || _document.DocumentElement == null ? null : GetEntry(_document.DocumentElement);
            ComputedFontMetrics parentFont = supplied.ParentFont with
            {
                Size = FontSize(parent) ?? supplied.ParentFont.Size ?? 16,
                LineHeight = LineHeight(parent) ?? supplied.ParentFont.LineHeight,
            };
            var viewport = new ComputedViewportMetrics(Media.Width, Media.Height);
            var smallViewport = new ComputedViewportMetrics(supplied.SmallViewport.Width ?? Media.Width,
                supplied.SmallViewport.Height ?? Media.Height);
            bool preferredDark = supplied.DarkColorScheme
                || Media.Features.TryGetValue("prefers-color-scheme", out string? scheme) && scheme == "dark";
            bool usedDark = UsedDarkColorScheme(specified.GetValueOrDefault("color-scheme"), preferredDark);
            // Preserve the supplied callback snapshot without capturing the entire large metrics/context struct.
            var suppliedNumeric = supplied.NumericValue;
            return supplied with
            {
                IsRoot = isRoot,
                IsFlexOrGridItem = HasFlexOrGridParent(parent),
                ParentFont = parentFont,
                Font = supplied.Font with { Size = supplied.Font.Size ?? parentFont.Size },
                RootFont = supplied.RootFont with { Size = FontSize(root) ?? supplied.RootFont.Size ?? 16, LineHeight = LineHeight(root) ?? supplied.RootFont.LineHeight },
                InitialFont = supplied.InitialFont with { Size = supplied.InitialFont.Size ?? 16 },
                MediumFontSize = supplied.MediumFontSize ?? 16,
                Viewport = supplied.Viewport.Width.HasValue ? supplied.Viewport : viewport,
                SmallViewport = smallViewport,
                LargeViewport = supplied.LargeViewport.Width.HasValue ? supplied.LargeViewport : viewport,
                DynamicViewport = supplied.DynamicViewport.Width.HasValue ? supplied.DynamicViewport : viewport,
                ViewportUnitRead = _observeViewportUnit,
                ContainerUnitBasis = ContainerUnits(entry, parent, specified.GetValueOrDefault("writing-mode"), supplied, smallViewport),
                // The composed callback handles absence per axis, including logical writing-mode mapping.
                NoEligibleContainer = false,
                DevicePixelRatio = supplied.DevicePixelRatio ?? Media.Resolution,
                DarkColorScheme = usedDark,
                SystemColor = supplied.SystemColor ?? (usedDark ? CssSystemColorPalette.Dark : CssSystemColorPalette.Light).Resolve,
                NumericValue = value => ReadNumericContext(entry, suppliedNumeric, value),
                ParentFontWeight = Numeric(parent?.Values.GetValueOrDefault("font-weight")) ?? supplied.ParentFontWeight ?? 400,
                InheritedColor = parent?.Values.GetValueOrDefault("color") ?? supplied.InheritedColor,
                PropertyValue = name => ReadOwnContext(entry, specified, name),
                ParentPropertyValue = name => ReadParentContext(entry, parent, name),
                DocumentUrl = entry.Element.NodeDocument.BaseUrl,
            };
        }
        // CSS Flexbox §4 / Grid §6: compute against the nearest composed ancestor
        // other than display:contents, even in hidden subtrees (computed, not used display).
        private bool HasFlexOrGridParent(Entry? parent)
        {
            while (parent != null)
            {
                if (parent.Values.GetValueOrDefault("display") is not CssKeywordValue display) return false;
                if (display.Value is "flex" or "inline-flex" or "grid" or "inline-grid") return true;
                if (!display.Is("contents")) return false;
                Element element = parent.Element;
                Element? ancestor = element.AssignedSlotInternal ?? element.ParentElement
                    ?? (element.ParentNode as ShadowRoot)?.Host;
                parent = ancestor == null ? null : GetEntry(ancestor);
            }
            return false;
        }
        private static bool UsedDarkColorScheme(CssValue? value, bool preferredDark)
        {
            if (value is CssKeywordValue keyword) return keyword.Is("dark");
            bool light = false, dark = false;
            if (value is CssListValue list)
                foreach (CssValue item in list.Items)
                {
                    if (item is CssKeywordValue word && word.Is("light")) light = true;
                    if (item is CssKeywordValue wordDark && wordDark.Is("dark")) dark = true;
                }
            return dark && (!light || preferredDark);
        }
        private static double? SiblingNumericValue(Element element, CssValue value)
        {
            if (value is not CssFunctionValue { Name: "sibling-index" or "sibling-count" } function) return null;
            int count = 0, index = 1;
            if (element.ParentNode == null) return 1;
            foreach (Node sibling in element.ParentNode.ChildNodes)
                if (sibling is Element)
                {
                    count++;
                    if (ReferenceEquals(sibling, element)) index = count;
                }
            return function.Name == "sibling-index" ? index : count;
        }
        private static void ComputeValue(string name, Entry entry, Dictionary<string, CssValue> specified, ref ComputedValueContext context)
        {
            if (!specified.TryGetValue(name, out var value)) return;
            entry.Values[name] = ComputedValueResolver.Resolve(name, value, context);
            if (name == "line-height") context = context with { Font = context.Font with { LineHeight = LineHeight(entry) ?? context.Font.LineHeight } };
        }
        private static double? FontSize(Entry? entry) => entry?.Values.GetValueOrDefault("font-size") is CssDimensionValue { Unit: "px" } size ? size.Value : null;
        private static double? LineHeight(Entry? entry)
        {
            CssValue? value = entry?.Values.GetValueOrDefault("line-height");
            if (value is CssDimensionValue { Unit: "px" } length) return length.Value;
            if (value is CssNumberValue number && FontSize(entry) is { } size) return number.Value * size;
            return null;
        }
        private static double? Numeric(CssValue? value)
        {
            if (value is CssNumberValue number) return number.Value;
            return value != null && double.TryParse(value.Serialize(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : null;
        }
        private IReadOnlyList<ComponentValue>? SubstituteFunction(Element element, FunctionValue function,
            CustomPropertyResolver variables, HashSet<string>? attributeStack)
        {
            if (function.Name.Equals("attr", StringComparison.OrdinalIgnoreCase))
            {
                if (!CssAttrParser.TryParse(function, out var value) || value is not CssAttrValue attribute
                    || !attribute.TryResolve(element.GetAttribute, out CssValue? resolved, (name, tokens) =>
                    {
                        HashSet<string> stack = attributeStack!;
                        if (!stack.Add(name)) return null;
                        try { return variables.Substitute(tokens, out _); }
                        finally { stack.Remove(name); }
                    }) || resolved == null) return null;
                return CssParser.ParseComponentValueList(resolved.Serialize());
            }
            if (!function.Name.Equals("env", StringComparison.OrdinalIgnoreCase)) return null;
            var arguments = function.Arguments;
            int start = 0;
            while (start < arguments.Count && arguments[start] is PreservedToken { Token.Kind: CssTokenKind.Whitespace }) start++;
            if (start == arguments.Count || arguments[start] is not PreservedToken { Token.Kind: CssTokenKind.Ident } name) return null;
            int comma = start + 1;
            while (comma < arguments.Count && arguments[comma] is not PreservedToken { Token.Kind: CssTokenKind.Comma }) comma++;
            string key = name.Token.Value;
            for (int i = start + 1; i < comma; i++)
                if (arguments[i] is PreservedToken { Token.Kind: CssTokenKind.Number } index) key += " " + index.Token.Value;
                else if (arguments[i] is not PreservedToken { Token.Kind: CssTokenKind.Whitespace }) return null;
            if (EnvironmentVariables.TryGetValue(key, out string? environment)) return CssParser.ParseComponentValueList(environment);
            if (comma == arguments.Count) return null;
            var fallback = new List<ComponentValue>(arguments.Count - comma - 1);
            for (int i = comma + 1; i < arguments.Count; i++) fallback.Add(arguments[i]);
            return fallback;
        }
        private ParsedProperty? ParseSubstituted(string name, IReadOnlyList<ComponentValue> tokens)
        {
            if (!IncrementalReconciliation)
            {
                SubstitutionParseCount++;
                return CssPropertyParser.Parse(name, tokens);
            }
            var parses = _substitutionParses.GetValue(tokens, static _ => new(StringComparer.Ordinal));
            if (parses.TryGetValue(name, out ParsedProperty? parsed)) return parsed;
            SubstitutionParseCount++;
            parsed = CssPropertyParser.Parse(name, tokens);
            parses.Add(name, parsed);
            return parsed;
        }
        // The supported property-name vocabulary is finite; custom names bypass this process-wide map.
        private static readonly ConcurrentDictionary<(string Name, bool Rtl), string> PhysicalNames = new();
        private static string PhysicalName(string name, bool rtl) => name.StartsWith("--", StringComparison.Ordinal)
            ? name : PhysicalNames.GetOrAdd((name, rtl), static key => ResolvePhysicalName(key.Name, key.Rtl));
        private static string ResolvePhysicalName(string name, bool rtl)
        {
            name = name switch
            {
                "inline-size" => "width", "block-size" => "height",
                "min-inline-size" => "min-width", "max-inline-size" => "max-width",
                "min-block-size" => "min-height", "max-block-size" => "max-height",
                "border-start-start-radius" => rtl ? "border-top-right-radius" : "border-top-left-radius",
                "border-start-end-radius" => rtl ? "border-top-left-radius" : "border-top-right-radius",
                "border-end-start-radius" => rtl ? "border-bottom-right-radius" : "border-bottom-left-radius",
                "border-end-end-radius" => rtl ? "border-bottom-left-radius" : "border-bottom-right-radius",
                _ => name,
            };
            name = name.Replace("block-start", "top", StringComparison.Ordinal).Replace("block-end", "bottom", StringComparison.Ordinal)
                .Replace("inline-start", rtl ? "right" : "left", StringComparison.Ordinal).Replace("inline-end", rtl ? "left" : "right", StringComparison.Ordinal);
            if (name.StartsWith("inset-", StringComparison.Ordinal)) name = name[6..];
            return name;
        }
    }
}
