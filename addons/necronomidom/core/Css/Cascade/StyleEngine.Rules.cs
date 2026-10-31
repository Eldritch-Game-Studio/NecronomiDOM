using System;
using System.Collections.Generic;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    public sealed partial class StyleEngine
    {
        private readonly record struct Candidate(string Name, CssValue Value, CascadePriority Priority, CascadeLayers.Layer Layer, Node Root);
        private sealed record Registration(string Syntax, bool Inherits, string Initial);
        private static readonly string[] ImageDimensionAttributes = { "width", "height" };
        private sealed class RuleCollection
        {
            internal readonly List<Candidate> Candidates = new();
            internal readonly Dictionary<(Node, CascadeOrigin), CascadeLayers> Layers = new();
            internal readonly Dictionary<string, Registration> Properties = new(StringComparer.Ordinal);
            internal readonly HashSet<CSSStyleSheet> ImportStack = new();
            internal readonly Dictionary<(CSSRule, CascadeLayers.Layer), CascadeLayers.Layer> RuleLayers = new();
            internal long Order;
            internal void Clear()
            {
                Candidates.Clear(); Layers.Clear(); Properties.Clear(); ImportStack.Clear(); RuleLayers.Clear(); Order = 0;
            }
        }
        private RuleCollection Collect(Entry entry)
        {
            var result = entry.Rules;
            if (entry.ReuseRules && entry.RuleGeneration == _generation)
            {
                entry.ReuseRules = false;
                result.Candidates.RemoveAll(static candidate => candidate.Priority.Inline);
                if (entry.Pseudo == null && entry.Element is HTMLElement inlineElement)
                {
                    Node root = inlineElement.Root;
                    var layer = result.Layers[(root, CascadeOrigin.Author)].Root;
                    AddDeclarations(inlineElement.Style, new Specificity(1, 0, 0), entry, result, layer,
                        CascadeOrigin.Author, root, Depth(root), int.MaxValue, true, result.Order);
                    for (int i = 0; i < result.Candidates.Count; i++)
                    {
                        var item = result.Candidates[i];
                        if (item.Priority.Inline)
                            result.Candidates[i] = item with { Priority = item.Priority with { Layer = item.Layer.Order } };
                    }
                }
                ReusedRuleCollectionCount++;
                return result;
            }
            entry.ReuseRules = false;
            entry.RuleGeneration = _generation;
            result.Clear();
            if (entry.Pseudo == null && entry.Element is HTMLImageElement)
            {
                Node root = entry.Element.Root;
                var hints = new CascadeLayers();
                result.Layers[(root, CascadeOrigin.Author)] = hints;
                foreach (string name in ImageDimensionAttributes)
                    if (ImageDimensionHint(entry.Element.GetAttribute(name)) is double dimension)
                        result.Candidates.Add(new Candidate(name, new CssDimensionValue(dimension, "px"),
                            new CascadePriority(CascadeOrigin.Author, false, int.MinValue, false, default, int.MaxValue, long.MinValue, Depth(root)), hints.Root, root));
            }
            foreach (SheetSource source in _sources)
            {
                bool userAgent = source.Sheet.Origin == CssOrigin.UserAgent;
                Node root = userAgent ? entry.Element.Root : source.Root;
                if (!userAgent && !ReferenceEquals(root, entry.Element.Root)
                    && !(root is ShadowRoot shadow && (ReferenceEquals(shadow.Host, entry.Element)
                        || ReferenceEquals(entry.Element.AssignedSlotInternal?.Root, root)))
                    && !SelectorMatcher.IsShadowDescendantOfScope(entry.Element, root)) continue;
                var origin = (CascadeOrigin)source.Sheet.Origin;
                if (!result.Layers.TryGetValue((root, origin), out var layers)) result.Layers[(root, origin)] = layers = new CascadeLayers();
                if (source.Sheet.Disabled || source.Sheet.Media.Length > 0 && !MatchesMedia(source.Sheet.Media.Queries, root)) continue;
                var context = new SelectorMatchContext(source.Root is ShadowRoot scopeRoot ? scopeRoot.Host : _document.DocumentElement,
                    Namespaces: source.Sheet.Namespaces, PseudoElement: entry.Pseudo, ShadowScope: source.Root as ShadowRoot,
                    TreeScope: root, MatchShadowElements: true);
                WalkRules(source.Sheet.CssRules, source, entry, result, layers, layers.Root, origin, context, null, int.MaxValue);
            }
            if (entry.Pseudo == null && entry.Element is HTMLElement html)
            {
                Node root = html.Root;
                if (!result.Layers.TryGetValue((root, CascadeOrigin.Author), out var layers)) result.Layers[(root, CascadeOrigin.Author)] = layers = new CascadeLayers();
                AddDeclarations(html.Style, new Specificity(1, 0, 0), entry, result, layers.Root,
                    CascadeOrigin.Author, root, Depth(root), int.MaxValue, true);
            }
            foreach (var layers in result.Layers.Values) layers.AssignOrder();
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                Candidate item = result.Candidates[i];
                if (item.Priority.Order != long.MinValue)
                    result.Candidates[i] = item with { Priority = item.Priority with { Layer = item.Layer.Order } };
            }
            return result;
        }
        internal long ReusedRuleCollectionCount { get; private set; }
        private static double? ImageDimensionHint(string? attribute)
        {
            if (attribute == null) return null;
            ReadOnlySpan<char> value = attribute.AsSpan().TrimStart(" \t\n\r\f");
            if (value.Length > 0 && value[0] == '+') value = value[1..];
            int count = 0;
            while (count < value.Length && value[count] is >= '0' and <= '9') count++;
            return count > 0 && double.TryParse(value[..count], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out double dimension) && double.IsFinite(dimension) ? dimension : null;
        }
        private void WalkRules(CSSRuleList rules, SheetSource source, Entry entry, RuleCollection result,
            CascadeLayers layers, CascadeLayers.Layer layer, CascadeOrigin origin,
            SelectorMatchContext context, SelectorList? nesting, int proximity)
        {
            MediaEnvironment environment = SourceMedia(source.Root);
            foreach (CSSRule rule in rules)
            {
                long order = ++result.Order;
                switch (rule)
                {
                    case CSSLayerStatementRule statement:
                        foreach (string name in statement.NameList) layers.Register(name, layer);
                        break;
                    case CSSLayerBlockRule block:
                        WalkRules(block.CssRules, source, entry, result, layers, RuleLayer(result, layers, block, layer, block.Name), origin, context, nesting, proximity);
                        break;
                    case CSSImportRule import:
                        CascadeLayers.Layer importedLayer = import.LayerName == null ? layer : RuleLayer(result, layers, import, layer, import.LayerName);
                        if (import.StyleSheet == null || import.Supports != null && !MediaEvaluator.Evaluate(import.Supports, environment)
                            || import.Media.Length > 0 && !MatchesMedia(import.Media.Queries, source.Root) || !result.ImportStack.Add(import.StyleSheet)) break;
                        WalkRules(import.StyleSheet.CssRules, source with { Sheet = import.StyleSheet }, entry, result, layers, importedLayer,
                            origin, context with { Namespaces = import.StyleSheet.Namespaces }, nesting, proximity);
                        result.ImportStack.Remove(import.StyleSheet);
                        break;
                    case CSSMediaRule media:
                        if (MatchesMedia(media.Media.Queries, source.Root)) WalkRules(media.CssRules, source, entry, result, layers, layer, origin, context, nesting, proximity);
                        break;
                    case CSSSupportsRule supports:
                        if (MediaEvaluator.Evaluate(supports.Condition, environment)) WalkRules(supports.CssRules, source, entry, result, layers, layer, origin, context, nesting, proximity);
                        break;
                    case CSSContainerRule container:
                        var containerEnvironment = ContainerEnvironment?.Invoke(entry.Element, container.Query.Name);
                        if (containerEnvironment != null && MediaEvaluator.Evaluate(container.Condition, containerEnvironment))
                            WalkRules(container.CssRules, source, entry, result, layers, layer, origin, context, nesting, proximity);
                        break;
                    case CSSScopeRule scope:
                        WalkScope(scope, source, entry, result, layers, layer, origin, context, nesting, proximity);
                        break;
                    case CSSStyleRule style:
                        SelectorList selectors = nesting == null ? style.Selectors : NestingExpansion.Expand(style.Selectors, nesting);
                        if (TrySpecificity(entry.Element, selectors, context, out Specificity specificity))
                            AddDeclarations(style.Style, specificity, entry, result, layer, origin, source.Root, source.Depth, proximity, false, order);
                        WalkRules(style.CssRules, source, entry, result, layers, layer, origin, context, selectors, proximity);
                        break;
                    case CSSNestedDeclarations declarations:
                        if (nesting != null && TrySpecificity(entry.Element, nesting, context, out Specificity nestedSpecificity))
                            AddDeclarations(declarations.Style, nestedSpecificity, entry, result, layer, origin, source.Root, source.Depth, proximity, false, order);
                        break;
                    case CSSPropertyRule property:
                        result.Properties[property.Name] = new Registration(property.Syntax.Trim('"'), property.Inherits, property.InitialValue);
                        break;
                }
            }
        }
        private void WalkScope(CSSScopeRule rule, SheetSource source, Entry entry, RuleCollection result,
            CascadeLayers layers, CascadeLayers.Layer layer, CascadeOrigin origin, SelectorMatchContext context,
            SelectorList? nesting, int outerDistance)
        {
            int distance = 0;
            for (Element? root = entry.Element; root != null; root = root.ParentElement, distance++)
            {
                bool start = rule.Start == null ? ReferenceEquals(root, source.Owner?.ParentElement ?? source.Root as Element ?? _document.DocumentElement)
                    : SelectorMatcher.Matches(root, rule.Start, context with { PseudoElement = null });
                if (!start) continue;
                var scoped = context with { Scope = root };
                bool excluded = false;
                if (rule.End != null)
                    for (Element? n = entry.Element; n != null && !ReferenceEquals(n, root); n = n.ParentElement)
                        if (SelectorMatcher.Matches(n, rule.End, scoped with { PseudoElement = null })) { excluded = true; break; }
                if (!excluded) WalkRules(rule.CssRules, source, entry, result, layers, layer, origin, scoped, nesting, System.Math.Min(distance, outerDistance));
            }
        }
        private static CascadeLayers.Layer RuleLayer(RuleCollection result, CascadeLayers layers, CSSRule rule,
            CascadeLayers.Layer parent, string name)
        {
            if (result.RuleLayers.TryGetValue((rule, parent), out var layer)) return layer;
            layer = layers.Register(name, parent);
            result.RuleLayers[(rule, parent)] = layer;
            return layer;
        }
        private static bool TrySpecificity(Element element, SelectorList list, SelectorMatchContext context, out Specificity specificity)
        {
            bool matched = false;
            specificity = default;
            foreach (var selector in list.Selectors)
            {
                if (!SelectorMatcher.MatchesComplex(element, selector, selector.Compounds.Count - 1, context)) continue;
                specificity = Specificity.Max(specificity, selector.Specificity);
                matched = true;
            }
            return matched;
        }
        private static void AddDeclarations(CSSStyleDeclaration declarations, Specificity specificity, Entry entry,
            RuleCollection result, CascadeLayers.Layer layer, CascadeOrigin origin, Node root, int depth, int proximity, bool inline, long? order = null)
        {
            long sourceOrder = order ?? ++result.Order;
            foreach (var declaration in declarations.EnumerateLonghands())
            {
                var priority = new CascadePriority(origin, declaration.Important, 0, inline, specificity, proximity, sourceOrder, depth);
                result.Candidates.Add(new Candidate(declaration.Name, declaration.Value, priority, layer, root));
            }
        }
    }
}
