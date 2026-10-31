using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    public sealed partial class StyleEngine
    {
        // CSS Conditional 5 § 7: select each physical axis independently in the flat tree.
        // Logical units use the querying element's axes, not the selected container's axes.
        private Func<string, double?> ContainerUnits(Entry entry, Entry? parent, CssValue? writingMode,
            ComputedValueContext supplied, ComputedViewportMetrics smallViewport)
        {
            bool vertical = VerticalWritingMode(writingMode);
            bool searched = false, widthEligible = false, heightEligible = false;
            double? widthBasis = null, heightBasis = null;
            // Only these supplied fields participate in deferred axis lookup, not the whole metrics struct.
            var suppliedBasis = supplied.ContainerUnitBasis;
            bool noEligibleContainer = supplied.NoEligibleContainer;
            return Basis;

            double? Basis(string unit)
            {
                entry.ComputedReuse?.ReadDynamic();
                double? provided = suppliedBasis?.Invoke(unit);
                if (provided.HasValue) return provided;
                bool width;
                switch (unit)
                {
                    case "cqw": width = true; break;
                    case "cqh": width = false; break;
                    case "cqi": width = !vertical; break;
                    case "cqb": width = vertical; break;
                    default: return null;
                }
                if (!noEligibleContainer && !searched)
                {
                    searched = true;
                    for (Entry? ancestor = parent; ancestor != null;)
                    {
                        // A reentrant refresh has incomplete computed properties: absence is not known.
                        if (ancestor.Refreshing) { widthEligible = heightEligible = true; break; }
                        CssValue? type = ancestor.Values.GetValueOrDefault("container-type");
                        if (CanContainSize(ancestor.Values.GetValueOrDefault("display")))
                        {
                            if (ContainerKeyword(type, "size"))
                            {
                                if (!widthEligible) { widthEligible = true; widthBasis = DefiniteBoxAxis(ancestor, true, false); }
                                if (!heightEligible) { heightEligible = true; heightBasis = DefiniteBoxAxis(ancestor, false, false); }
                                break;
                            }
                            if (ContainerKeyword(type, "inline-size"))
                            {
                                if (VerticalWritingMode(ancestor.Values.GetValueOrDefault("writing-mode")))
                                {
                                    if (!heightEligible) { heightEligible = true; heightBasis = DefiniteBoxAxis(ancestor, false, false); }
                                }
                                else if (!widthEligible) { widthEligible = true; widthBasis = DefiniteBoxAxis(ancestor, true, false); }
                                if (widthEligible && heightEligible) break;
                            }
                        }
                        Element? element = ancestor.Element.AssignedSlotInternal ?? ancestor.Element.ParentElement
                            ?? (ancestor.Element.ParentNode as ShadowRoot)?.Host;
                        ancestor = element == null ? null : GetEntry(element);
                    }
                }
                if (!noEligibleContainer && (width ? widthEligible : heightEligible)) return width ? widthBasis : heightBasis;
                return width ? smallViewport.Width : smallViewport.Height;
            }
        }
        // A definite, constrained CSS size is usable without inventing an auto/intrinsic layout box.
        // Container units use the content box; transform percentages use their selected reference box.
        private static double? DefiniteBoxAxis(Entry entry, bool width, bool borderBox)
        {
            string axis = width ? "width" : "height";
            if (entry.Values.GetValueOrDefault(axis) is not CssDimensionValue { Unit: "px" } dimension
                || !double.IsFinite(dimension.Value)) return null;
            double size = dimension.Value;
            CssValue? minimum = entry.Values.GetValueOrDefault(width ? "min-width" : "min-height");
            CssValue? maximum = entry.Values.GetValueOrDefault(width ? "max-width" : "max-height");
            if (minimum is CssDimensionValue { Unit: "px" } min) size = System.Math.Max(size, min.Value);
            else if (minimum is not (null or CssKeywordValue { Value: "auto" })) return null;
            if (maximum is CssDimensionValue { Unit: "px" } max) size = System.Math.Min(size, max.Value);
            else if (maximum is not (null or CssKeywordValue { Value: "none" })) return null;
            if (minimum is CssDimensionValue { Unit: "px" } floor) size = System.Math.Max(size, floor.Value);
            double? start = width
                ? BoxEdge(entry, "padding-left", "border-left-style", "border-left-width")
                : BoxEdge(entry, "padding-top", "border-top-style", "border-top-width");
            double? end = width
                ? BoxEdge(entry, "padding-right", "border-right-style", "border-right-width")
                : BoxEdge(entry, "padding-bottom", "border-bottom-style", "border-bottom-width");
            if (!start.HasValue || !end.HasValue) return null;
            double edges = start.Value + end.Value;
            bool specifiedBorderBox = entry.Values.GetValueOrDefault("box-sizing") is CssKeywordValue { Value: "border-box" };
            double content = System.Math.Max(0, specifiedBorderBox ? size - edges : size);
            double result = borderBox ? content + edges : content;
            return double.IsFinite(result) ? result : null;
        }
        private static double? BoxEdge(Entry entry, string paddingName, string styleName, string widthName)
        {
            if (entry.Values.GetValueOrDefault(paddingName) is not CssDimensionValue { Unit: "px" } padding) return null;
            if (entry.Values.GetValueOrDefault(styleName) is CssKeywordValue { Value: "none" or "hidden" })
                return padding.Value;
            return entry.Values.GetValueOrDefault(widthName) is CssDimensionValue { Unit: "px" } border
                ? padding.Value + border.Value : null;
        }

        private static bool VerticalWritingMode(CssValue? value)
            => value is CssKeywordValue { Value: "vertical-rl" or "vertical-lr" or "sideways-rl" or "sideways-lr" };

        private static bool CanContainSize(CssValue? display)
        {
            // Size containment does not apply to non-atomic inline or table/internal table boxes.
            if (display is CssKeywordValue keyword)
                return keyword.Value is not ("none" or "contents" or "inline" or "inline-table" or "table"
                    or "table-row" or "table-row-group" or "table-header-group" or "table-footer-group"
                    or "table-column" or "table-column-group" or "table-cell" or "ruby" or "ruby-base"
                    or "ruby-text" or "ruby-base-container" or "ruby-text-container");
            return !ContainerKeyword(display, "table") && (!ContainerKeyword(display, "inline")
                || ContainerKeyword(display, "flow-root") || ContainerKeyword(display, "flex") || ContainerKeyword(display, "grid"));
        }

        private static bool ContainerKeyword(CssValue? value, string keyword)
        {
            if (value is CssKeywordValue word) return word.Is(keyword);
            if (value is CssListValue { Separator: CssListSeparator.Space } list)
                for (int i = 0; i < list.Items.Count; i++)
                    if (list.Items[i] is CssKeywordValue item && item.Is(keyword)) return true;
            return false;
        }
    }
}
