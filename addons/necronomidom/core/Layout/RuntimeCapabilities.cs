using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NecronomiDom.Layout;

/// <summary>
/// The bounded renderer's admission definitions, shared with the capability reference generator.
/// Admission does not guarantee that every value in the parser grammar can be laid out or painted.
/// Context-dependent constraints remain enforced by the layout and paint solvers.
/// </summary>
public static class RuntimeCapabilities
{
    private static readonly Profile Data = Load();

    public static IReadOnlyList<string> Elements => Data.Elements.Names;
    public static IReadOnlyList<string> InlineElements => Data.InlineElements.Names;
    public static IReadOnlyList<string> Properties => Data.Properties.Names;
    public static IReadOnlyList<string> PseudoClasses => Data.PseudoClasses.Names;
    public static IReadOnlyList<string> StylesheetRuleTypes => Data.RuleTypes.Names;
    public static IReadOnlyList<string> KeywordProperties => Data.KeywordProperties;

    public static bool AdmitsElement(string name) => Data.Elements.Set.Contains(name);
    public static bool AdmitsInlineElement(string name) => Data.InlineElements.Set.Contains(name);
    public static bool AdmitsProperty(string name) => Data.Properties.Set.Contains(name);
    public static bool AdmitsPseudoClass(string name) => Data.PseudoClasses.Set.Contains(name);
    public static bool AdmitsStylesheetRule(string typeName) => Data.RuleTypes.Set.Contains(typeName);

    /// <summary>Unconstrained properties return an empty list, not a promise of arbitrary value support.</summary>
    public static IReadOnlyList<string> AllowedKeywords(string property) =>
        Data.Keywords.TryGetValue(property, out var names) ? names.Names : Array.Empty<string>();

    /// <summary>Tests only the exact keyword restrictions shared by generic element validation.</summary>
    public static bool AdmitsKeyword(string property, string value) =>
        Data.Keywords.TryGetValue(property, out var names) && names.Set.Contains(value);

    private sealed class NameSet
    {
        internal readonly HashSet<string> Set;
        internal readonly IReadOnlyList<string> Names;
        internal NameSet(List<string> names)
        {
            Set = new HashSet<string>(names, StringComparer.Ordinal);
            if (Set.Count != names.Count) throw new InvalidOperationException("Duplicate runtime capability name.");
            Names = names.AsReadOnly();
        }
    }

    private sealed class Profile
    {
        internal required NameSet Elements, InlineElements, Properties, PseudoClasses, RuleTypes;
        internal required IReadOnlyList<string> KeywordProperties;
        internal readonly Dictionary<string, NameSet> Keywords = new(StringComparer.Ordinal);
    }

    private static Profile Load()
    {
        using Stream stream = typeof(RuntimeCapabilities).Assembly.GetManifestResourceStream("NecronomiDom.runtime-capabilities.json")
            ?? throw new InvalidOperationException("Missing embedded runtime capability definitions.");
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unknown runtime capability schema.");
        var properties = new List<string>();
        foreach (JsonElement group in root.GetProperty("groups").EnumerateArray())
            properties.AddRange(ReadNames(group.GetProperty("properties")));
        var keywordProperties = new List<string>();
        var profile = new Profile
        {
            Elements = new NameSet(ReadNames(root.GetProperty("elements"))),
            InlineElements = new NameSet(ReadNames(root.GetProperty("inlineElements"))),
            Properties = new NameSet(properties),
            PseudoClasses = new NameSet(ReadNames(root.GetProperty("pseudoClasses"))),
            RuleTypes = new NameSet(ReadNames(root.GetProperty("stylesheetRuleTypes"))),
            KeywordProperties = keywordProperties.AsReadOnly()
        };
        foreach (JsonProperty property in root.GetProperty("keywords").EnumerateObject())
        {
            keywordProperties.Add(property.Name);
            profile.Keywords.Add(property.Name, new NameSet(ReadNames(property.Value)));
        }
        return profile;
    }

    private static List<string> ReadNames(JsonElement values)
    {
        var names = new List<string>(values.GetArrayLength());
        foreach (JsonElement value in values.EnumerateArray())
            names.Add(value.GetString() ?? throw new InvalidOperationException("Null runtime capability name."));
        return names;
    }
}
