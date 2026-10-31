using System;
using System.Collections.Generic;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Om
{
    internal static partial class CSSRuleParser
    {
        private static CSSRule? AtRule(RawAtRule raw, CSSStyleSheet? sheet, CSSRule? parent, bool nested, IList<WebDiagnostic>? diagnostics, string file, string? source)
        {
            var name = Dom.Names.AsciiLower(raw.Name);
            var prelude = CssParser.Trim(raw.Prelude);
            if (raw.Block == null)
            {
                if (parent != null && name is "import" or "namespace" or "custom-media") return null;
                return name switch
                {
                    "import" => Import(prelude, source),
                    "namespace" => Namespace(prelude),
                    "layer" => LayerNames(prelude) is { Count: > 0 } names ? new CSSLayerStatementRule(names) : null,
                    "custom-media" => CustomMedia(prelude),
                    _ => null,
                };
            }
            CSSRule? result;
            switch (name)
            {
                case "media":
                    if (prelude.Count == 0) return null;
                    var media = MediaQueryParser.Parse(prelude);
                    if (diagnostics != null) foreach (var query in media.Queries)
                    {
                        if (!query.IsValid) Warn(diagnostics, file, raw, "Invalid media query replaced with not all.");
                        else if (HasUnrecognizedCondition(query.Condition)) Warn(diagnostics, file, raw, "Unrecognized media condition retained as general-enclosed syntax.");
                    }
                    result = new CSSMediaRule(media);
                    break;
                case "supports":
                    var supports = SupportsParser.Parse(prelude, new SelectorParseOptions(Namespaces: sheet?.Namespaces) { SourceText = source });
                    if (supports == null) return null;
                    if (diagnostics != null && HasUnrecognizedCondition(supports)) Warn(diagnostics, file, raw, "Unrecognized supports condition retained as general-enclosed syntax.");
                    result = new CSSSupportsRule(supports);
                    break;
                case "container":
                    var container = ContainerQueryParser.Parse(prelude);
                    if (container == null) return null;
                    result = new CSSContainerRule(container);
                    if (diagnostics != null && HasUnrecognizedCondition(container.Condition)) Warn(diagnostics, file, raw, "Unrecognized container condition retained as general-enclosed syntax.");
                    break;
                case "layer":
                    var layer = prelude.Count == 0 ? "" : LayerName(prelude);
                    if (layer == null) return null;
                    result = new CSSLayerBlockRule(layer);
                    break;
                case "scope":
                    result = Scope(prelude, sheet, source);
                    if (result == null) return null;
                    break;
                case "starting-style":
                    if (prelude.Count != 0) return null;
                    result = new CSSStartingStyleRule();
                    break;
                case "keyframes":
                case "-webkit-keyframes":
                    if (nested || prelude.Count != 1 || prelude[0] is not PreservedToken keyName || keyName.Token.Kind is not (CssTokenKind.String or CssTokenKind.Ident)) return null;
                    if (keyName.Token.Kind == CssTokenKind.Ident && Reserved(keyName.Token.Value, true)) return null;
                    result = new CSSKeyframesRule(keyName.Token.Value, keyName.Token.Kind == CssTokenKind.String);
                    break;
                case "font-face":
                    if (nested || prelude.Count != 0) return null;
                    var font = new CSSFontFaceRule();
                    font.Style.Load(CssParser.ParseBlockContents(raw.Block.Value, diagnostics, file), diagnostics, file);
                    return font;
                case "counter-style":
                    if (nested || IdentName(prelude) is not string counter || Reserved(counter, true)) return null;
                    var counterRule = new CSSCounterStyleRule(counter);
                    counterRule.Style.Load(CssParser.ParseBlockContents(raw.Block.Value, diagnostics, file), diagnostics, file);
                    return ValidCounter(counterRule) ? counterRule : null;
                case "position-try":
                    if (nested || IdentName(prelude) is not string position || !position.StartsWith("--", StringComparison.Ordinal) || position.Length == 2) return null;
                    var positionRule = new CSSPositionTryRule(position);
                    positionRule.Style.Load(CssParser.ParseBlockContents(raw.Block.Value, diagnostics, file), diagnostics, file);
                    return positionRule;
                case "property":
                    if (nested || IdentName(prelude) is not string property || !property.StartsWith("--", StringComparison.Ordinal) || property.Length == 2) return null;
                    var registration = new CSSPropertyRule(property);
                    registration.Style.Load(CssParser.ParseBlockContents(raw.Block.Value, diagnostics, file), diagnostics, file);
                    return ValidProperty(registration) ? registration : null;
                default: return null;
            }
            if (result is CSSGroupingRule group) PopulateGroup(group, raw.Block, sheet, parent, diagnostics, file, source);
            return result;
        }
        private static bool HasUnrecognizedCondition(QueryCondition? condition)
        {
            switch (condition)
            {
                case GeneralEnclosedCondition: return true;
                case NotCondition not: return HasUnrecognizedCondition(not.Condition);
                case GroupCondition group: return HasUnrecognizedCondition(group.Condition);
                case FunctionCondition function: return HasUnrecognizedCondition(function.Condition);
                case JunctionCondition junction:
                    foreach (var child in junction.Conditions) if (HasUnrecognizedCondition(child)) return true;
                    break;
            }
            return false;
        }
        private static string? IdentName(IReadOnlyList<ComponentValue> values) => values.Count == 1 && values[0] is PreservedToken token && token.Token.Kind == CssTokenKind.Ident ? token.Token.Value : null;
        private static bool Reserved(string name, bool none = false) => Dom.Names.AsciiLower(name) is "initial" or "inherit" or "unset" or "revert" or "revert-layer" or "default" || (none && Dom.Names.AsciiEqual(name, "none"));
        private static CSSScopeRule? Scope(IReadOnlyList<ComponentValue> values, CSSStyleSheet? sheet, string? source)
        {
            var p = 0;
            SelectorList? start = null;
            SelectorList? end = null;
            if (p < values.Count && values[p] is SimpleBlock block && block.Opener == CssTokenKind.LeftParen)
            {
                start = SelectorParser.ParseSelectorList(block.Value, new SelectorParseOptions(AllowNesting: true, AllowPseudoElements: false, Namespaces: sheet?.Namespaces) { SourceText = source });
                if (start == null) return null;
                p++;
            }
            while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++;
            if (p < values.Count)
            {
                if (!CssParser.IsIdent(values[p++], "to")) return null;
                while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++;
                if (p >= values.Count || values[p++] is not SimpleBlock limit || limit.Opener != CssTokenKind.LeftParen) return null;
                end = SelectorParser.ParseSelectorList(limit.Value, new SelectorParseOptions(AllowNesting: true, AllowPseudoElements: false, Namespaces: sheet?.Namespaces) { SourceText = source });
                if (end == null) return null;
                while (p < values.Count && CssParser.Is(values[p], CssTokenKind.Whitespace)) p++;
            }
            return p == values.Count ? new CSSScopeRule(start, end) : null;
        }
    }
}
