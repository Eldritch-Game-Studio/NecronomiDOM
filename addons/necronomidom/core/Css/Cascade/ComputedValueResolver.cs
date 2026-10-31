using System;
using System.Collections.Generic;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Math;
using NecronomiDom.Url;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Cascade 5 § 4.4 computed values, Values 4 units/math, and property-defined layout-independent computation.</summary>
    public static partial class ComputedValueResolver
    {
        public static CssValue Resolve(string property, CssValue specified, ComputedValueContext context)
        {
            // Own one public input snapshot: a provider may reassign its caller's context variable.
            // Internal dispatch borrows this snapshot; escaping callbacks still retain owned copies.
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(specified);
            if (property.StartsWith("--", StringComparison.Ordinal))
                return specified is CssUnparsedValue ? specified : Normalize(specified, property, context);
            PropertyDefinition definition = CssPropertyRegistry.Get(property)
                ?? throw new ArgumentException("Property is outside the CSS profile.", nameof(property));
            if (definition.IsShorthand) throw new ArgumentException("Resolve requires a longhand, not a shorthand.", nameof(property));
            if (specified is CssWideKeyword or CssPendingSubstitutionValue)
                throw new ArgumentException("Cascade and substitution must precede computed-value resolution.", nameof(specified));
            property = definition.Name;
            CssValue result = Normalize(specified, property, context);
            result = FontValue(property, result, context);
            result = PropertyValue(property, result, definition, context);
            result = Constrain(result, definition.Grammar, property);
            return Canonical(property, result, context);
        }

        private static CssValue Normalize(CssValue value, string property, in ComputedValueContext context)
        {
            switch (value)
            {
                case CssSystemFontValue systemFont:
                    CssValue? system = context.SystemFont?.Invoke(systemFont.SystemFont, systemFont.Longhand);
                    return system == null ? value : Normalize(system, property, context);
                case CssDimensionValue dimension: return Dimension(dimension, property, context);
                case CssMathValue math: return MathValue(math, property, context);
                case CssSizingCalculationValue sizing: return NormalizeSizing(sizing, property, context);
                case CssPercentageValue percent when IsNumberPercentage(property): return new CssNumberValue(percent.Value / 100, false);
                case CssColorValue color: return ColorValue(color, property, context);
                case CssPaletteMixValue palette: return NormalizePaletteMix(palette, property, context);
                case CssListValue list:
                    return MapList(list, property, context);
                case CssFunctionValue function when function.Arguments != null:
                    CssValue args = Normalize(function.Arguments, property, context);
                    return ShapeFunction(function, args, property, context);
                case CssBlockValue block when block.Content != null:
                    CssValue content = Normalize(block.Content, property, context);
                    return ReferenceEquals(content, block.Content) ? value : new CssBlockValue(content);
                case CssUrlValue url when context.DocumentUrl != null:
                    WebUrl? parsed = WebUrl.Parse(url.Url, context.DocumentUrl);
                    return parsed == null || parsed.Href == url.Url ? value : new CssUrlValue(parsed.Href, url.CrossOrigin, url.Integrity, url.ReferrerPolicy);
                default: return NormalizeImage(value, property, context);
            }
        }

        // A context-capturing callback belongs only to calc-size normalization. Keeping it
        // outside Normalize avoids allocating its large display class for every keyword/number.
        private static CssValue NormalizeSizing(CssSizingCalculationValue sizing, string property, ComputedValueContext context) =>
            sizing.Simplify(new CssMathEvaluationContext(unit => UnitScale(unit, property, context),
                devicePixelRatio: context.DevicePixelRatio, resolveNumericValue: context.NumericValue));

        private static CssValue MapList(CssListValue list, string property, in ComputedValueContext context)
        {
            CssValue[]? changed = null;
            for (int i = 0; i < list.Items.Count; i++)
            {
                CssValue item = Normalize(list.Items[i], property, context);
                if (changed == null && !ReferenceEquals(item, list.Items[i]))
                {
                    changed = new CssValue[list.Items.Count];
                    for (int j = 0; j < i; j++) changed[j] = list.Items[j];
                }
                if (changed != null) changed[i] = item;
            }
            return changed == null ? list : new CssListValue(changed, list.Separator);
        }


        private static bool Assign(CssResolvedColor color, out CssResolvedColor target) { target = color; return true; }
        private static CssListValue Space(params CssValue[] values) => new(values, CssListSeparator.Space);
        private static CssKeywordValue Keyword(string value) => new(value);
        private static readonly CssDimensionValue ZeroPixels = new(0, "px"), OnePixel = new(1, "px"),
            ThreePixels = new(3, "px"), FivePixels = new(5, "px");
        private static CssDimensionValue Pixels(double value) => value switch
        {
            0 when BitConverter.DoubleToInt64Bits(value) == 0 => ZeroPixels,
            1 => OnePixel, 3 => ThreePixels, 5 => FivePixels,
            _ => new(value, "px")
        };
        // Borrow a scalar or existing list, rather than materializing a singleton array.
        // No interface implementation/boxing: internal normalizers only need Count/indexing.
        private readonly struct ValueItems
        {
            private readonly CssValue? _single;
            private readonly IReadOnlyList<CssValue>? _list;
            internal ValueItems(CssValue single) { _single = single; _list = null; }
            internal ValueItems(IReadOnlyList<CssValue> list) { _single = null; _list = list; }
            internal int Count => _list?.Count ?? 1;
            internal CssValue this[int index] => _list != null ? _list[index]
                : index == 0 ? _single! : throw new IndexOutOfRangeException();
        }
        private static ValueItems Items(CssValue value) =>
            value is CssListValue { Separator: CssListSeparator.Space } list ? new(list.Items) : new(value);
    }
}
