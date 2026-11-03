using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade;

/// <summary>Actual matching CSSOM source. CSSOM does not retain rule/declaration line or column offsets.</summary>
public sealed record InspectedStyleSource(long Order, string Selector, string SourcePath, string RuleText, bool Inline);
/// <summary>A matched expanded declaration, sorted by cascade precedence within its physical property.
/// Winner means the cascaded declaration after revert/revert-layer, not necessarily a valid computed value.</summary>
public sealed record InspectedDeclaration(string Property, string DeclaredProperty, string Value,
    CascadePriority Priority, bool Winner, long SourceOrder);
public sealed record InspectedProperty(string Name, string Value);
/// <summary>Raw input at this element and final computed result, including inherited and invalid variables.
/// Null Resolved means guaranteed-invalid/missing; an empty string is a valid empty custom value.</summary>
public sealed record InspectedCustomProperty(string Name, string Raw, string? Resolved, bool LocallyDeclared);
public sealed record StyleInspection(IReadOnlyList<InspectedStyleSource> Sources,
    IReadOnlyList<InspectedDeclaration> Declarations, IReadOnlyList<InspectedProperty> Computed,
    IReadOnlyList<InspectedCustomProperty> CustomProperties);

public sealed partial class StyleEngine
{
    /// <summary>Explicit, allocation-bearing developer snapshot. Reuses the real selector/media/scope/layer
    /// collector and cascade winner algorithm, without enabling observation on the normal style hot path.</summary>
    public StyleInspection Inspect(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!ReferenceEquals(element.OwnerDocument, _document))
            throw new ArgumentException("The inspected element belongs to another document.", nameof(element));
        if (!element.IsConnected) throw new ArgumentException("Inspect a connected element.", nameof(element));
        Entry current = GetEntry(element);
        var probe = new Entry(this, element, null);
        var inspectionSources = new Dictionary<long, CSSStyleDeclaration>();
        probe.Rules.InspectionSources = inspectionSources;
        RuleCollection rules = Collect(probe);
        bool rtl = current.Declaration.GetPropertyValue("direction") == "rtl";
        var groups = Group(rules.Candidates, rtl, probe);
        var declarations = new List<InspectedDeclaration>();
        var names = new List<string>(groups.Keys);
        names.Sort(StringComparer.Ordinal);
        foreach (string name in names)
        {
            var candidates = groups[name];
            int winner = WinnerIndex(candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                declarations.Add(new InspectedDeclaration(name, candidate.Name, candidate.Value.Serialize(),
                    candidate.Priority, i == winner, candidate.Priority.Order));
            }
        }
        var sources = new List<InspectedStyleSource>();
        foreach (var pair in inspectionSources)
        {
            CSSRule? rule = pair.Value.ParentRule;
            CSSStyleSheet? sheet = rule?.ParentStyleSheet;
            string selector = rule is CSSStyleRule style ? style.SelectorText
                : rule is CSSNestedDeclarations ? "<nested declarations>" : "element.style";
            sources.Add(new InspectedStyleSource(pair.Key, selector,
                sheet?.SourcePath is { Length: > 0 } path ? path : sheet?.Href ??
                    (rule == null ? "<inline style attribute>" : "<constructed/inline stylesheet>"),
                rule?.CssText ?? pair.Value.CssText, rule == null));
        }
        sources.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        var computed = new List<InspectedProperty>();
        var customNames = new HashSet<string>(current.RawCustom.Keys, StringComparer.Ordinal);
        for (int i = 0; i < current.Declaration.Length; i++)
        {
            string name = current.Declaration.Item(i);
            computed.Add(new InspectedProperty(name, current.Declaration.GetPropertyValue(name)));
            if (name.StartsWith("--", StringComparison.Ordinal)) customNames.Add(name);
        }
        foreach (string name in groups.Keys)
            if (name.StartsWith("--", StringComparison.Ordinal)) customNames.Add(name);
        foreach (var declaration in declarations)
            CollectVariableReferences(CssParser.ParseComponentValueList(declaration.Value), customNames);
        foreach (var tokens in current.RawCustom.Values) CollectVariableReferences(tokens, customNames);
        computed.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        var custom = new List<InspectedCustomProperty>();
        foreach (string name in customNames)
        {
            bool valid = current.Values.TryGetValue(name, out var resolved);
            string raw = current.RawCustom.TryGetValue(name, out var tokens) ? CssTokenSerializer.Serialize(tokens) : "";
            custom.Add(new InspectedCustomProperty(name, raw, valid ? resolved!.Serialize() : null, groups.ContainsKey(name)));
        }
        custom.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        return new StyleInspection(sources.AsReadOnly(), declarations.AsReadOnly(), computed.AsReadOnly(), custom.AsReadOnly());
    }

    private static void CollectVariableReferences(IReadOnlyList<ComponentValue> tokens, HashSet<string> names)
    {
        foreach (var token in tokens)
        {
            if (token is FunctionValue function)
            {
                if (function.Name.Equals("var", StringComparison.OrdinalIgnoreCase))
                    foreach (var argument in function.Arguments)
                    {
                        if (argument is PreservedToken { Token.Kind: CssTokenKind.Whitespace }) continue;
                        if (argument is PreservedToken { Token.Kind: CssTokenKind.Ident } ident
                            && ident.Token.Value.StartsWith("--", StringComparison.Ordinal)) names.Add(ident.Token.Value);
                        break;
                    }
                CollectVariableReferences(function.Arguments, names);
            }
            else if (token is SimpleBlock block) CollectVariableReferences(block.Value, names);
        }
    }
}
