using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.TypedOm;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    public sealed partial class StyleEngine
    {
        /// <summary>Typed OM exposes computed values, unlike CSSOM getComputedStyle's resolved-value view (CSSOM § 9).</summary>
        public CSSStyleDeclaration GetComputedValues(Element element)
        {
            var entry = GetEntry(element);
            if (entry.ComputedDeclaration == null)
            {
                entry.ComputedDeclaration = new CSSStyleDeclaration { ReadOnly = true, IsComputed = true };
                entry.ComputedDeclaration.BeforeRead = entry.Declaration.BeforeRead;
                entry.ComputedDeclaration.ReplaceComputed(Declarations(entry, resolved: false));
            }
            return entry.ComputedDeclaration;
        }
        private List<CssDeclaration> Declarations(Entry entry, bool resolved)
        {
            var result = entry.Projection;
            var target = resolved ? entry.Declaration : entry.ComputedDeclaration!;
            // Projection order is already ordinal-sorted. Reuse it only when every key still exists,
            // not merely when counts match (one custom name can replace another in the same batch).
            if (IncrementalReconciliation && result.Count == entry.Values.Count)
            {
                bool sameNames = true;
                for (int i = 0; i < result.Count; i++)
                {
                    string name = result[i].Name;
                    if (!entry.Values.TryGetValue(name, out CssValue? value)) { sameNames = false; break; }
                    result[i] = target.ReuseComputedDeclaration(i, name, resolved ? ResolvedValue(entry, name, value) : value);
                }
                if (sameNames) return result;
            }
            result.Clear();
            foreach (var pair in entry.Values)
                result.Add(target.ReuseComputedDeclaration(-1, pair.Key, resolved ? ResolvedValue(entry, pair.Key, pair.Value) : pair.Value));
            result.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
            return result;
        }
        private static CssValue ResolvedValue(Entry entry, string property, CssValue value)
        {
            if (property == "line-height" && value is CssNumberValue number
                && entry.Values.TryGetValue("font-size", out var size) && size is CssDimensionValue { Unit: "px" } font)
                return new CssDimensionValue(number.Value * font.Value, "px");
            if (property == "caret-color" && value is CssKeywordValue { Value: "auto" }
                && entry.Values.TryGetValue("color", out var color)) return color;
            if (property is "transform-origin" or "perspective-origin" && value is CssListValue origin)
            {
                bool borderBox = property == "perspective-origin"
                    || entry.Values.GetValueOrDefault("transform-box") is not CssKeywordValue { Value: "content-box" };
                CssValue[]? coordinates = null;
                for (int i = 0; i < origin.Items.Count; i++)
                {
                    CssValue resolved = UsedLength(origin.Items[i], i < 2 ? DefiniteBoxAxis(entry, i == 0, borderBox) : null);
                    if (!ReferenceEquals(resolved, origin.Items[i]) && coordinates == null)
                    {
                        coordinates = new CssValue[origin.Items.Count];
                        for (int j = 0; j < coordinates.Length; j++) coordinates[j] = origin.Items[j];
                    }
                    if (coordinates != null) coordinates[i] = resolved;
                }
                return coordinates == null ? value : new CssListValue(coordinates, origin.Separator);
            }
            if (property == "transform") value = UsedTranslations(entry, value);
            if (property != "transform" || !TypedTransformDecoder.TryDecode(value, out var transform)) return value;
            DOMMatrix matrix;
            try { matrix = transform.ToMatrix(); }
            catch (DomTypeError)
            {
                // The resolved-value host has no layout/font magnitude for an unresolved relative translation.
                // Retain its real computed graph; never manufacture a containing-block or font basis.
                return value;
            }
            CssValue[] entries;
            if (matrix.Is2D)
                entries = new CssValue[] { Number(matrix.M11), Number(matrix.M12), Number(matrix.M21),
                    Number(matrix.M22), Number(matrix.M41), Number(matrix.M42) };
            else
                entries = new CssValue[] { Number(matrix.M11), Number(matrix.M12), Number(matrix.M13), Number(matrix.M14),
                    Number(matrix.M21), Number(matrix.M22), Number(matrix.M23), Number(matrix.M24),
                    Number(matrix.M31), Number(matrix.M32), Number(matrix.M33), Number(matrix.M34),
                    Number(matrix.M41), Number(matrix.M42), Number(matrix.M43), Number(matrix.M44) };
            return new CssFunctionValue(matrix.Is2D ? "matrix" : "matrix3d", new CssListValue(entries, CssListSeparator.Comma));
        }
        private static CssValue UsedLength(CssValue value, double? basis)
        {
            double result;
            if (value is CssPercentageValue percentage && basis.HasValue) result = percentage.Value * (basis.Value / 100);
            else if (value is CssMathValue math
                && math.TryEvaluate(new CssMathEvaluationContext(percentageBasis: basis, percentageHint: CssNumericDimension.Length), out result)) { }
            else return value;
            result = double.IsNaN(result) ? 0 : System.Math.Clamp(result, -double.MaxValue, double.MaxValue);
            return new CssDimensionValue(result, "px");
        }
        private static CssValue UsedTranslations(Entry entry, CssValue value)
        {
            if (value is CssListValue list)
            {
                CssValue[]? changed = null;
                for (int i = 0; i < list.Items.Count; i++)
                {
                    CssValue item = UsedTranslations(entry, list.Items[i]);
                    if (changed == null && !ReferenceEquals(item, list.Items[i]))
                    {
                        changed = new CssValue[list.Items.Count];
                        for (int j = 0; j < changed.Length; j++) changed[j] = list.Items[j];
                    }
                    if (changed != null) changed[i] = item;
                }
                return changed == null ? value : new CssListValue(changed, list.Separator);
            }
            if (value is not CssFunctionValue { Arguments: not null } function
                || !function.Name.StartsWith("translate", StringComparison.OrdinalIgnoreCase)) return value;
            bool borderBox = entry.Values.GetValueOrDefault("transform-box") is not CssKeywordValue { Value: "content-box" };
            double? width = DefiniteBoxAxis(entry, true, borderBox), height = DefiniteBoxAxis(entry, false, borderBox);
            if (function.Arguments is not CssListValue arguments)
            {
                CssValue resolved = UsedLength(function.Arguments,
                    function.Name.Equals("translateY", StringComparison.OrdinalIgnoreCase) ? height : width);
                return ReferenceEquals(resolved, function.Arguments) ? value : new CssFunctionValue(function.Name, resolved);
            }
            CssValue[]? replacements = null;
            for (int i = 0; i < arguments.Items.Count; i++)
            {
                CssValue resolved = UsedLength(arguments.Items[i], i == 0 ? width : i == 1 ? height : null);
                if (replacements == null && !ReferenceEquals(resolved, arguments.Items[i]))
                {
                    replacements = new CssValue[arguments.Items.Count];
                    for (int j = 0; j < replacements.Length; j++) replacements[j] = arguments.Items[j];
                }
                if (replacements != null) replacements[i] = resolved;
            }
            return replacements == null ? value : new CssFunctionValue(function.Name, new CssListValue(replacements, arguments.Separator));
        }
        private static CssNumberValue Number(double value) => new(value, value == System.Math.Truncate(value));
    }
}
