using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade;

public sealed partial class StyleEngine
{
    private readonly record struct GeometryInlineValue(CssDeclaration Declaration, bool Won);
    internal long GeometryRefreshCount { get; private set; }


    private static void CaptureGeometryInline(Entry entry, Dictionary<string, List<Candidate>> groups)
    {
        entry.GeometryInline?.Clear();
        if (!entry.InlineGeometryOnly || entry.Element is not HTMLElement html || html.Style.Length == 0) return;
        entry.GeometryInline ??= new();
        foreach (var declaration in html.Style.Declarations)
        {
            bool won = groups.TryGetValue(declaration.Name, out var candidates) &&
                ReferenceEquals(Winner(candidates), declaration.Value);
            entry.GeometryInline.Add(new(declaration, won));
        }
    }

    /// <summary>Consumes a winning numeric inline-opacity-only change whose existing invalidation
    /// guards prove that no other computed style depends on it. A painter must still retain the
    /// actual opacity group and rebuild for transition or stacking-context changes.</summary>
    public bool TryConsumeInlineOpacityChange(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_entries.TryGetValue(element, out var entries) || entries.Normal is not { } entry) return false;
        Refresh(entry);
        if (!entry.InlineOpacityChanged) return false;
        entry.InlineOpacityChanged = false;
        return true;
    }
    /// <summary>Consumes a guarded independent inline transform delta; the layout still admits only an existing carrier.</summary>
    public bool TryConsumeInlineTransformChange(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_entries.TryGetValue(element, out var entries) || entries.Normal is not { } entry) return false;
        Refresh(entry);
        if (!entry.InlineTransformChanged) return false;
        entry.InlineTransformChanged = false;
        return true;
    }

    private static bool InlineCompositorOnlyChange(Entry entry, bool transform)
    {
        if (entry.Element is not HTMLElement html || entry.GeometryInline == null) return false;
        var current = html.Style.Declarations;
        if (current.Count != entry.GeometryInline.Count) return false;
        bool changed = false;
        for (int i = 0; i < current.Count; i++)
        {
            var previous = entry.GeometryInline[i];
            var next = current[i];
            if (next.Name != previous.Declaration.Name || next.Important != previous.Declaration.Important) return false;
            if (ReferenceEquals(next.Value, previous.Declaration.Value)) continue;
            bool property = transform ? TransformProperty(next.Name) : next.Name == "opacity";
            if (!property || !previous.Won || !StableInlineValue(next.Value) ||
                !IndependentGeometry(next.Name, next.Value)) return false;
            changed = true;
        }
        return changed;
    }

    // Only a previously winning inline declaration at the identical cascade priority
    // can bypass rule collection. Add/remove/reorder, importance, inheritance, variables,
    // relative units and context-sensitive values use the full existing cascade.
    private bool TryRefreshGeometry(Entry entry)
    {
        var current = ((HTMLElement)entry.Element).Style.Declarations;
        if (entry.GeometryInline == null || current.Count != entry.GeometryInline.Count) return false;
        for (int i = 0; i < current.Count; i++)
        {
            var prior = entry.GeometryInline[i];
            var next = current[i];
            if (next.Name != prior.Declaration.Name || next.Important != prior.Declaration.Important) return false;
            if (!StableInlineValue(next.Value)) return false;
            if (!ReferenceEquals(next.Value, prior.Declaration.Value) && (!prior.Won || !IndependentGeometry(next.Name, next.Value)))
                return false;
        }
        bool rtl = entry.Values.GetValueOrDefault("direction") is CssKeywordValue { Value: "rtl" };
        for (int i = 0; i < current.Count; i++)
        {
            var next = current[i];
            if (ReferenceEquals(next.Value, entry.GeometryInline[i].Declaration.Value)) continue;
            var computed = ComputedValueResolver.Resolve(next.Name, next.Value, default);
            ReplaceGeometryValue(entry, next.Name, computed);
            // The existing cascade exposes physical and logical longhands together.
            string? alias = next.Name switch
            {
                "width" => "inline-size", "height" => "block-size",
                "top" => "inset-block-start", "bottom" => "inset-block-end",
                "left" => rtl ? "inset-inline-end" : "inset-inline-start",
                "right" => rtl ? "inset-inline-start" : "inset-inline-end",
                _ => null
            };
            if (alias != null) ReplaceGeometryValue(entry, alias, computed);
            entry.GeometryInline[i] = new(next, true);
        }
        // CSSOM resolves these against this element's definite width/height even
        // though their computed (Typed OM) values have not changed.
        foreach (string property in GeometryResolvedProperties)
            if (entry.Values.TryGetValue(property, out var value))
                entry.Declaration.ReplaceComputedValue(property, ResolvedValue(entry, property, value));
        GeometryRefreshCount++;
        return true;
    }

    private static readonly string[] GeometryResolvedProperties = { "transform", "transform-origin", "perspective-origin" };

    private static void ReplaceGeometryValue(Entry entry, string name, CssValue value)
    {
        entry.Values[name] = value;
        entry.Declaration.ReplaceComputedValue(name, value);
        entry.ComputedDeclaration?.ReplaceComputedValue(name, value);
    }

    private static bool StableInlineValue(CssValue value)
    {
        if (value is CssKeywordValue || value.GetType() == typeof(CssNumberValue) ||
            value.GetType() == typeof(CssIntegerValue) || value.GetType() == typeof(CssPercentageValue)) return true;
        if (value.GetType() == typeof(CssDimensionValue)) return value is CssDimensionValue { Unit: "px" or "deg" };
        if (value.GetType() == typeof(CssListValue) && value is CssListValue list)
        {
            foreach (var item in list.Items) if (!StableInlineValue(item)) return false;
            return true;
        }
        return value is CssFunctionValue function && function.Name is
            ("matrix" or "translate" or "translatex" or "translatey" or "rotate" or "scale" or "scalex" or "scaley" or "skew" or "skewx" or "skewy")
            && function.Arguments != null && StableInlineValue(function.Arguments);
    }
    private static bool TransformProperty(string property) =>
        property is "translate" or "rotate" or "scale" or "transform" or "transform-origin";

    private static bool IndependentGeometry(string property, CssValue value)
    {
        // The guarded transform subset contains no inherited or external unit bases.
        if (TransformProperty(property)) return StableInlineValue(value);
        if (property is not ("left" or "right" or "top" or "bottom" or "width" or "height" or "opacity" or "rotate")) return false;
        return value.GetType() == typeof(CssNumberValue) || value.GetType() == typeof(CssIntegerValue) ||
            value.GetType() == typeof(CssPercentageValue) ||
            value.GetType() == typeof(CssDimensionValue) && value is CssDimensionValue { Unit: "px" or "deg" } ||
            value is CssKeywordValue { Value: "auto" or "none" };
    }
}
