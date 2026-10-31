using System.Collections.Generic;
using NecronomiDom.Css.Media;
using NecronomiDom.Css.Om;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    public sealed partial class StyleEngine
    {
        private readonly Dictionary<Node, MediaEnvironment> _scopeMedia = new();
        private MediaEnvironment SourceMedia(Node root)
        {
            if (_scopeMedia.TryGetValue(root, out var found)) return found;
            var environment = new MediaEnvironment
            {
                Type = Media.Type, Width = Media.Width, Height = Media.Height, Resolution = Media.Resolution,
                RootFontSize = Media.RootFontSize, UnitScale = Media.UnitScale, ContainerStyle = Media.ContainerStyle,
            };
            environment.Features.Clear();
            foreach (var feature in Media.Features) environment.Features[feature.Key] = feature.Value;
            foreach (var custom in Media.CustomMedia) environment.CustomMedia[custom.Key] = custom.Value;
            _scopeMedia[root] = environment;
            var visited = new HashSet<CSSStyleSheet>();
            foreach (SheetSource source in _sources)
                if (ReferenceEquals(source.Root, root) && source.Sheet.Origin != CssOrigin.UserAgent)
                    CollectCustomMedia(source.Sheet, environment, visited);
            return environment;
        }
        private static void CollectCustomMedia(CSSStyleSheet sheet, MediaEnvironment environment, HashSet<CSSStyleSheet> visited)
        {
            if (!visited.Add(sheet) || sheet.Disabled) return;
            foreach (CSSRule rule in sheet.CssRules)
            {
                if (rule is CSSCustomMediaRule custom) environment.CustomMedia[custom.Name] = custom.BooleanValue.HasValue
                    ? new BooleanCondition(custom.BooleanValue.Value) : new MediaQueryCondition(custom.Queries!);
                else if (rule is CSSImportRule { StyleSheet: { } imported }) CollectCustomMedia(imported, environment, visited);
            }
        }
    }
}
