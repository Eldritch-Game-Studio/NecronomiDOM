using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade;

public sealed partial class StyleEngine
{
    private bool _geometryStyleSensitive;

    // These affect used geometry/paint, not inherited font/color/custom values. Layout still runs normally:
    // descendant percentages resolve against the new boxes, and em values keep the unchanged inherited font.
    private static bool GeometryProperty(string name) => name is "left" or "right" or "top" or "bottom" or
        "width" or "height" or "translate" or "rotate" or "scale" or "transform" or "transform-origin" or "opacity" or "display";

    private static bool InlineGeometryOnly(Element element)
    {
        if (element is not HTMLElement html) return false;
        var declarations = html.Style.Declarations;
        for (int i = 0; i < declarations.Count; i++)
            if (!GeometryProperty(declarations[i].Name)) return false;
        return true;
    }

    private bool TryInvalidateInlineGeometry(Element element)
    {
        if (_geometryStyleSensitive || ValueContext != null || ContainerEnvironment != null ||
            !_entries.TryGetValue(element, out var entries) || entries.Normal?.InlineGeometryOnly != true ||
            !InlineGeometryOnly(element) || !InlineDisplayUnchanged(entries.Normal) || DescendantInheritsGeometry(element)) return false;
        bool canRefresh = entries.Normal.Generation == _generation || entries.Normal.GeometryRefreshGeneration == _generation;
        bool opacityOnly = InlineCompositorOnlyChange(entries.Normal, transform: false);
        bool transformOnly = InlineCompositorOnlyChange(entries.Normal, transform: true);
        // An originating element is the inheritance parent of its own generated pseudos.
        // Their paint must also update when they explicitly inherit opacity.
        foreach (var pseudo in entries.Pseudos.Values)
            if (pseudo.ReadsParentGeometry) opacityOnly = transformOnly = false;
        entries.MarkStale();
        if (canRefresh) entries.Normal.GeometryRefreshGeneration = _generation;
        entries.Normal.InlineOpacityChanged = canRefresh && opacityOnly;
        entries.Normal.InlineTransformChanged = canRefresh && transformOnly;
        return true;
    }

    // A changed display declaration changes descendants' computed blockification, not just used boxes.
    // Keep scalar animation fast when an unchanged inline display accompanies width/opacity/etc.
    private static bool InlineDisplayUnchanged(Entry entry)
    {
        CssDeclaration? before = null, after = null;
        if (entry.GeometryInline != null)
            for (int i = 0; i < entry.GeometryInline.Count; i++)
                if (entry.GeometryInline[i].Declaration.Name == "display") { before = entry.GeometryInline[i].Declaration; break; }
        var current = ((HTMLElement)entry.Element).Style.Declarations;
        for (int i = 0; i < current.Count; i++)
            if (current[i].Name == "display") { after = current[i]; break; }
        return ReferenceEquals(before?.Value, after?.Value) && before?.Important == after?.Important;
    }

    private bool DescendantInheritsGeometry(Node node)
    {
        for (Node? child = node.FirstChild; child != null; child = child.NextSibling)
        {
            if (child is Element element && _entries.TryGetValue(element, out var entries))
            {
                if (entries.Normal?.ReadsParentGeometry == true) return true;
                foreach (var pseudo in entries.Pseudos.Values)
                    if (pseudo.ReadsParentGeometry) return true;
            }
            if (DescendantInheritsGeometry(child)) return true;
        }
        return false;
    }

    private void ScanGeometryRule(CSSRule rule)
    {
        if (_geometryStyleSensitive) return;
        if (rule is CSSContainerRule or CSSPropertyRule) { _geometryStyleSensitive = true; return; }
        var declarations = rule switch
        {
            CSSStyleRule style => style.Style.Declarations,
            CSSNestedDeclarations nested => nested.Style.Declarations,
            _ => null
        };
        if (declarations == null) return;
        for (int i = 0; i < declarations.Count; i++)
            if (ReadsAttribute(declarations[i].Value)) { _geometryStyleSensitive = true; return; }
    }

    private static bool ReadsAttribute(CssValue value)
    {
        switch (value)
        {
            case CssUnparsedValue unparsed: return ReadsAttribute(unparsed.Tokens);
            case CssPendingSubstitutionValue pending: return ReadsAttribute(pending.Tokens);
            case CssFunctionValue function:
                return function.Name.Equals("attr", StringComparison.OrdinalIgnoreCase) ||
                    function.Arguments != null && ReadsAttribute(function.Arguments);
            case CssBlockValue { Content: { } content }: return ReadsAttribute(content);
            case CssListValue list:
                for (int i = 0; i < list.Items.Count; i++) if (ReadsAttribute(list.Items[i])) return true;
                return false;
            case CssKeywordValue or CssNumberValue or CssDimensionValue or CssPercentageValue or CssStringValue or CssUrlValue:
                return false;
            // Other value families (colors, images, math) may embed attr(). Scan only when sheets change.
            default: return value.Serialize().Contains("attr(", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool ReadsAttribute(IReadOnlyList<ComponentValue> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is FunctionValue function && (function.Name.Equals("attr", StringComparison.OrdinalIgnoreCase) ||
                ReadsAttribute(function.Arguments))) return true;
            if (tokens[i] is SimpleBlock block && ReadsAttribute(block.Value)) return true;
        }
        return false;
    }
}
