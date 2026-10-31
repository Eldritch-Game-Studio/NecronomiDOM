using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    public sealed partial class StyleEngine
    {
        private static readonly ConcurrentDictionary<string, CssValue> InitialValues = new(StringComparer.Ordinal);
        // Cache syntax, never computed values: em/currentcolor/URLs still resolve in each element's context.
        // Weak token identity bounds changing palettes; ephemerons also permit a parsed value to retain its tokens.
        private readonly ConditionalWeakTable<IReadOnlyList<ComponentValue>, Dictionary<string, ParsedProperty?>> _substitutionParses = new();
        private void Compute(Entry entry)
        {
            entry.InlineGeometryOnly = InlineGeometryOnly(entry.Element);
            entry.ReadsParentGeometry = false;
            RuleCollection rules = Collect(entry);
            Element? parentElement = entry.Pseudo != null ? entry.Element : entry.Element.AssignedSlotInternal
                ?? entry.Element.ParentElement ?? (entry.Element.ParentNode as ShadowRoot)?.Host;
            Entry? parent = parentElement == null ? null : GetEntry(parentElement);
            var groups = Group(rules.Candidates, false, entry);
            CssValue direction = Specified("direction", groups, parent, out _);
            bool rtl = direction is CssKeywordValue { Value: "rtl" };
            if (rtl) groups = Group(rules.Candidates, true, entry);
            var rawCustom = entry.RawCustom;
            rawCustom.Clear();
            // Unregistered custom properties this element does not declare inherit the parent's computed value as is.
            var passedCustom = entry.PassedCustom;
            passedCustom.Clear();
            if (parent != null)
                foreach (var pair in parent.Values)
                {
                    if (!pair.Key.StartsWith("--", StringComparison.Ordinal)) continue;
                    bool registered = rules.Properties.TryGetValue(pair.Key, out Registration? registration);
                    if (registered && !registration!.Inherits) continue;
                    rawCustom[pair.Key] = InheritedTokens(pair.Value);
                    if (!registered && pair.Value is CssUnparsedValue) passedCustom[pair.Key] = pair.Value;
                }
            foreach (var pair in rules.Properties)
                if (!rawCustom.ContainsKey(pair.Key) && pair.Value.Initial.Length > 0) rawCustom[pair.Key] = CssParser.ParseComponentValueList(pair.Value.Initial);
            foreach (var pair in groups)
            {
                if (!pair.Key.StartsWith("--", StringComparison.Ordinal)) continue;
                passedCustom.Remove(pair.Key);
                CssValue? value = Winner(pair.Value);
                if (value is CssWideKeyword wide)
                {
                    bool inherits = !rules.Properties.TryGetValue(pair.Key, out Registration? registration) || registration.Inherits;
                    if (wide.Kind == CssWideKeywordKind.Inherit || wide.Kind == CssWideKeywordKind.Unset && inherits)
                    {
                        if (parent != null && parent.Values.TryGetValue(pair.Key, out var inherited)) rawCustom[pair.Key] = InheritedTokens(inherited);
                        else rawCustom.Remove(pair.Key);
                    }
                    else if (rules.Properties.TryGetValue(pair.Key, out var initialRegistration) && initialRegistration.Initial.Length > 0)
                        rawCustom[pair.Key] = CssParser.ParseComponentValueList(initialRegistration.Initial);
                    else rawCustom.Remove(pair.Key);
                }
                else if (value is CssUnparsedValue unparsed) rawCustom[pair.Key] = unparsed.Tokens;
            }
            CustomPropertyResolver? variables = entry.Variables;
            HashSet<string>? attributeStack = null;
            Func<FunctionValue, IReadOnlyList<ComponentValue>?> substitute = function =>
                SubstituteFunction(entry.Element, function, variables!,
                    Dom.Names.AsciiEqual(function.Name, "attr") ? attributeStack ??= new(StringComparer.Ordinal) : null);
            Func<string, IReadOnlyList<ComponentValue>?, IReadOnlyList<ComponentValue>?> registerValue =
                (name, tokens) => RegisteredTokens(name, tokens, rules.Properties, parent);
            var inheritedCustom = IncrementalReconciliation ? passedCustom : null;
            if (variables == null || !IncrementalReconciliation)
                variables = new CustomPropertyResolver(rawCustom, substitute, registerValue, inheritedCustom, IncrementalReconciliation);
            else variables.Reset(substitute, registerValue, inheritedCustom);
            entry.Variables = variables;
            var specified = entry.Specified;
            specified.EnsureCapacity(ComputedLonghands.Value.Length);
            // One substituted shorthand serves all of its longhands; it is not re-substituted/re-parsed per longhand.
            var substitutedDeclarations = IncrementalReconciliation ? entry.SubstitutedDeclarations : null;
            substitutedDeclarations?.Clear();
            ComputeSpecifiedInputs(entry, parent, groups, variables, rtl, ref substitutedDeclarations);
            ComputedValueContext context = Context(entry, parent, specified);
            ComputeValue("font-size", entry, specified, ref context);
            if (entry.Values.TryGetValue("font-size", out var fontSize) && fontSize is CssDimensionValue { Unit: "px" } size)
                context = context with { Font = context.Font with { Size = size.Value } };
            ComputeValue("line-height", entry, specified, ref context);
            if (entry.ForegroundExpression != null)
                entry.ForegroundExpression = ComputedValueResolver.Resolve("color", entry.ForegroundExpression, context with { InheritedColor = null });
            ComputeValue("color", entry, specified, ref context);
            context = context with { CurrentColor = entry.Values.GetValueOrDefault("color") };
            ComputeRemainingValues(entry, parent, specified, context);
            foreach (var pair in rawCustom)
            {
                if (passedCustom.TryGetValue(pair.Key, out CssValue? passed)) { entry.Values[pair.Key] = passed; continue; }
                var tokens = variables.Resolve(pair.Key);
                if (tokens == null) continue;
                CssValue value = tokens is InheritedCustomTokens inheritedTokens
                    ? inheritedTokens.Value : new CssUnparsedValue(tokens);
                if (rules.Properties.TryGetValue(pair.Key, out var registration) && registration.Syntax != "*")
                {
                    bool valid = CssRegistrationSyntax.TryParseValue(registration.Syntax, tokens, out CssValue? registered);
                    if (!valid)
                    {
                        if (registration.Inherits && parent?.Values.TryGetValue(pair.Key, out var inherited) == true) { entry.Values[pair.Key] = inherited; continue; }
                        tokens = CssParser.ParseComponentValueList(registration.Initial);
                        valid = CssRegistrationSyntax.TryParseValue(registration.Syntax, tokens, out registered);
                    }
                    if (!valid || registered == null) continue;
                    value = ComputedValueResolver.Resolve(pair.Key, registered, context);
                }
                entry.Values[pair.Key] = value;
            }
            CaptureGeometryInline(entry, groups);
            entry.ComputedReuse?.Complete();
        }
        private static IReadOnlyList<ComponentValue>? RegisteredTokens(string name, IReadOnlyList<ComponentValue>? tokens,
            Dictionary<string, Registration> registrations, Entry? parent)
        {
            if (!registrations.TryGetValue(name, out var registration)) return tokens;
            if (tokens != null && (registration.Syntax == "*"
                || CssRegistrationSyntax.TryParseValue(registration.Syntax, tokens, out _))) return tokens;
            if (registration.Inherits && parent?.Values.TryGetValue(name, out var inherited) == true)
                return inherited is CssUnparsedValue raw ? raw.Tokens : CssParser.ParseComponentValueList(inherited.Serialize());
            return registration.Initial.Length == 0 ? null : CssParser.ParseComponentValueList(registration.Initial);
        }
        private Entry GetEntry(Element element)
        {
            GetComputedStyle(element);
            return _entries.GetValue(element, static _ => new EntrySet()).Normal!;
        }
        private static Dictionary<string, List<Candidate>> Group(List<Candidate> candidates, bool rtl, Entry entry)
        {
            var groups = entry.Groups;
            groups.Clear();
            foreach (var buffer in entry.GroupBuffers) buffer.Clear();
            int used = 0;
            foreach (Candidate candidate in candidates)
            {
                string name = PhysicalName(candidate.Name, rtl);
                if (!groups.TryGetValue(name, out var values))
                {
                    if (used == entry.GroupBuffers.Count) entry.GroupBuffers.Add(new List<Candidate>());
                    values = entry.GroupBuffers[used++];
                    groups[name] = values;
                }
                values.Add(candidate);
            }
            foreach (var values in groups.Values) values.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return groups;
        }
        private static CssValue? Winner(List<Candidate> candidates)
        {
            int maximumOrigin = (int)CascadeOrigin.Author;
            HashSet<(CascadeOrigin, Node, int, bool, int)>? revertedLayers = null;
            foreach (ref readonly Candidate candidate in CollectionsMarshal.AsSpan(candidates))
            {
                var priority = candidate.Priority;
                if ((int)priority.Origin > maximumOrigin || revertedLayers?.Contains((priority.Origin, candidate.Root, priority.Layer, priority.Important, priority.EncapsulationDepth)) == true) continue;
                if (candidate.Value is CssWideKeyword { Kind: CssWideKeywordKind.Revert }) { maximumOrigin = (int)priority.Origin - 1; continue; }
                if (candidate.Value is CssWideKeyword { Kind: CssWideKeywordKind.RevertLayer })
                { (revertedLayers ??= new()).Add((priority.Origin, candidate.Root, priority.Layer, priority.Important, priority.EncapsulationDepth)); continue; }
                return candidate.Value;
            }
            return null;
        }
        private static CssValue Specified(string name, Dictionary<string, List<Candidate>> groups, Entry? parent, out CssValue? cascaded) =>
            Specified(LonghandInputFor(name), groups, parent, out cascaded);
        private static CssValue Specified(in LonghandInput property, Dictionary<string, List<Candidate>> groups, Entry? parent, out CssValue? cascaded)
        {
            CssValue? value = cascaded = groups.TryGetValue(property.Name, out var candidates) ? Winner(candidates) : null;
            if (value is not CssWideKeyword keyword) return value ?? Unset(property, parent);
            return keyword.Kind == CssWideKeywordKind.Initial ? property.Initial
                : keyword.Kind == CssWideKeywordKind.Inherit ? InheritedValue(property.Name, parent) ?? property.Initial : Unset(property, parent);
        }
        private static CssValue Unset(string name, Entry? parent) => Unset(LonghandInputFor(name), parent);
        private static CssValue Unset(in LonghandInput property, Entry? parent) =>
            property.Inherited ? InheritedValue(property.Name, parent) ?? property.Initial : property.Initial;
        private static CssValue Initial(string name)
        {
            // Hot path: every unset longhand of every element; a published value is immutable and never replaced.
            if (InitialValues.TryGetValue(name, out var published)) return published;
            lock (InitialValues)
            {
                if (InitialValues.TryGetValue(name, out var cached)) return cached;
                PropertyDefinition definition = CssPropertyRegistry.Get(name) ?? throw new InvalidOperationException("Missing property metadata: " + name);
                string text = name == "font-family" ? "serif" : definition.Initial;
                ParsedProperty? parsed = CssPropertyParser.Parse(name, CssParser.ParseComponentValueList(text));
                CssValue value = ParsedLonghand(parsed, PhysicalName(definition.Name, false), false)
                    ?? throw new InvalidOperationException("Invalid initial value metadata for " + name + ": " + text);
                InitialValues[name] = value;
                return value;
            }
        }
        private static CssValue? ParsedLonghand(ParsedProperty? parsed, string name, bool rtl)
        {
            if (parsed == null) return null;
            foreach (var pair in parsed.Longhands) if (PhysicalName(pair.Longhand, rtl) == name) return pair.Value;
            return null;
        }
        private static bool ContainsUrl(IReadOnlyList<ComponentValue> tokens)
        {
            foreach (var token in tokens)
                if (token is PreservedToken { Token.Kind: CssTokenKind.Url } || token is FunctionValue f && (f.Name.Equals("url", StringComparison.OrdinalIgnoreCase) || ContainsUrl(f.Arguments))
                    || token is SimpleBlock block && ContainsUrl(block.Value)) return true;
            return false;
        }
    }
}
