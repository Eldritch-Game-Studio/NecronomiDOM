using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace NecronomiDom.Css.Values.Registry
{
    /// <summary>One webref property record (<c>Css/Data/webref-css.json</c>), before profile filtering.</summary>
    internal sealed record WebrefProperty(
        string Name,
        string? Syntax,
        string? Initial,
        string? Inherited,
        string? AnimationType,
        string? ComputedValue,
        string? LogicalGroup,
        string? LegacyAliasOf,
        IReadOnlyList<string> Longhands,
        IReadOnlyList<string> ResetLonghands);

    /// <summary>A webref value type or function definition; <see cref="For"/> scopes it (empty = global).</summary>
    internal sealed record WebrefSyntax(string Name, IReadOnlyList<string> For, string Syntax);

    /// <summary>An at-rule descriptor definition.</summary>
    internal sealed record WebrefDescriptor(string Name, string? Syntax, string? Initial);

    /// <summary>
    /// Loads the generated webref CSS data and the profile list (embedded resources
    /// <c>NecronomiDom.webref-css.json</c>, <c>NecronomiDom.supported-properties.json</c>) once per process.
    /// </summary>
    internal sealed class WebrefData
    {
        private static readonly Lazy<WebrefData> Loaded = new(Load);

        private WebrefData()
        {
        }

        public static WebrefData Current => Loaded.Value;

        public Dictionary<string, WebrefProperty> Properties { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<WebrefSyntax>> Types { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<WebrefSyntax>> Functions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, WebrefDescriptor>> AtRules { get; } = new(StringComparer.Ordinal);
        public List<string> Supported { get; } = new();
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

        private static WebrefData Load()
        {
            var data = new WebrefData();
            using (JsonDocument css = JsonDocument.Parse(ReadResource("NecronomiDom.webref-css.json")))
            {
                JsonElement root = css.RootElement;
                foreach (JsonElement p in root.GetProperty("properties").EnumerateArray())
                {
                    var prop = new WebrefProperty(
                        Str(p, "name")!, Str(p, "syntax"), Str(p, "initial"), Str(p, "inherited"), Str(p, "animationType"),
                        Str(p, "computedValue"), Str(p, "logicalPropertyGroup"), Str(p, "legacyAliasOf"), Strs(p, "longhands"), Strs(p, "resetLonghands"));
                    data.Properties[prop.Name] = prop;
                }

                AddSyntaxes(root.GetProperty("types"), data.Types);
                AddSyntaxes(root.GetProperty("functions"), data.Functions);
                foreach (JsonElement a in root.GetProperty("atrules").EnumerateArray())
                {
                    var descriptors = new Dictionary<string, WebrefDescriptor>(StringComparer.Ordinal);
                    foreach (JsonElement d in a.GetProperty("descriptors").EnumerateArray())
                    {
                        string name = Str(d, "name")!;
                        descriptors.TryAdd(name, new WebrefDescriptor(name, Str(d, "syntax"), Str(d, "initial")));
                    }

                    data.AtRules[Str(a, "name")!.TrimStart('@')] = descriptors;
                }
            }

            using (JsonDocument profile = JsonDocument.Parse(ReadResource("NecronomiDom.supported-properties.json")))
            {
                foreach (JsonElement n in profile.RootElement.GetProperty("properties").EnumerateArray()) data.Supported.Add(n.GetString()!);
                foreach (JsonProperty a in profile.RootElement.GetProperty("aliases").EnumerateObject()) data.Aliases[a.Name] = a.Value.GetString()!;
            }

            return data;
        }

        private static void AddSyntaxes(JsonElement array, Dictionary<string, List<WebrefSyntax>> into)
        {
            foreach (JsonElement t in array.EnumerateArray())
            {
                var entry = new WebrefSyntax(Str(t, "name")!, Strs(t, "for"), Str(t, "syntax")!);
                if (!into.TryGetValue(entry.Name, out List<WebrefSyntax>? list)) into[entry.Name] = list = new List<WebrefSyntax>(1);
                list.Add(entry);
            }
        }

        private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) ? v.GetString() : null;

        private static IReadOnlyList<string> Strs(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out JsonElement v)) return Array.Empty<string>();
            var list = new List<string>(v.GetArrayLength());
            foreach (JsonElement s in v.EnumerateArray()) list.Add(s.GetString()!);
            return list;
        }

        private static byte[] ReadResource(string name)
        {
            Assembly asm = typeof(WebrefData).Assembly;
            using Stream stream = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException("missing embedded resource " + name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
