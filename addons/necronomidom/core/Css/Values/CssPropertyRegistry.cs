using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Registry;

namespace NecronomiDom.Css.Values
{
    /// <summary>A CSS property definition from pinned webref; CSS Values 4 § 2 and Cascade 5 § 7.</summary>
    public sealed class PropertyDefinition
    {
        internal PropertyDefinition(WebrefProperty source)
        {
            Name = source.Name;
            Syntax = BrowserSyntax(source);
            Grammar = GrammarParser.Parse(Syntax);
            Initial = source.Name switch { "animation-delay" => "0s", "font-family" => "serif", _ => source.Initial ?? "" };
            Inherited = NecronomiDom.Dom.Names.AsciiEqual(source.Inherited, "yes");
            Longhands = source.Name == "text-box" ? new[] { "text-box-trim", "text-box-edge" } : source.Name == "all" ? AllProperties()
                : source.Name is "box-shadow" or "text-shadow" or "animation-delay" or "-webkit-line-clamp" or "overflow-clip-margin" or "text-align"
                    ? Array.Empty<string>() : source.Longhands;
            ResetLonghands = source.Name == "background" ? Array.Empty<string>() : source.ResetLonghands;
            LogicalGroup = source.LogicalGroup;
            AnimationType = source.AnimationType ?? "";
            ComputedValue = source.ComputedValue ?? "";
        }

        public string Name { get; }
        public string Syntax { get; }
        public string Initial { get; }
        public bool Inherited { get; }
        public bool IsShorthand => Longhands.Count != 0;
        public IReadOnlyList<string> Longhands { get; }
        public IReadOnlyList<string> ResetLonghands { get; }
        public string? LogicalGroup { get; }
        public string AnimationType { get; }
        public string ComputedValue { get; }
        internal GrammarNode Grammar { get; }

        private static IReadOnlyList<string> AllProperties()
        {
            var properties = new List<string>();
            foreach (string name in WebrefData.Current.Supported)
                if (name is not ("all" or "direction" or "unicode-bidi")) properties.Add(name);
            return properties;
        }

        private static string BrowserSyntax(WebrefProperty p) => p.Name switch
        {
            // Webref tracks drafts which split these shipped longhands into unshipped components.
            "animation-delay" => "<time>#",
            "box-shadow" => "<spread-shadow># | none",
            "text-shadow" => "<shadow># | none",
            "-webkit-line-clamp" => "none | <integer [1,∞]>",
            "font-synthesis" => "none | [ weight || [ style | oblique-only ] || small-caps || position ]",
            _ => p.Syntax ?? throw new InvalidOperationException("Property has no grammar: " + p.Name),
        };
    }

    /// <summary>The complete NecronomiDOM property profile, generated from webref (CSS Values 4 § 2).</summary>
    public static class CssPropertyRegistry
    {
        private static readonly Lazy<Dictionary<string, PropertyDefinition>> Definitions = new(Load);
        private static readonly ConcurrentDictionary<string, PropertyDefinition> ReferenceDefinitions = new(StringComparer.Ordinal);
        public static IEnumerable<string> SupportedNames => WebrefData.Current.Supported;

        public static PropertyDefinition? Get(string name)
        {
            name = NecronomiDom.Dom.Names.AsciiLower(name);
            if (WebrefData.Current.Aliases.TryGetValue(name, out string? target)) name = target;
            return Definitions.Value.TryGetValue(name, out PropertyDefinition? definition) ? definition : null;
        }

        internal static PropertyDefinition? GetUnfiltered(string name)
        {
            if (Get(name) is PropertyDefinition found) return found;
            return WebrefData.Current.Properties.TryGetValue(name, out WebrefProperty? source) && source.Syntax != null
                ? ReferenceDefinitions.GetOrAdd(name, _ => new PropertyDefinition(source)) : null;
        }

        private static Dictionary<string, PropertyDefinition> Load()
        {
            var result = new Dictionary<string, PropertyDefinition>(StringComparer.Ordinal);
            foreach (string name in WebrefData.Current.Supported)
                result.Add(name, new PropertyDefinition(WebrefData.Current.Properties[name]));
            return result;
        }
    }
}
