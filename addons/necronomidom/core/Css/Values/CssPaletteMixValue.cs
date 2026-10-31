using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    /// <summary>A symbolic palette selector and its optional specified or computed percentage.</summary>
    public readonly record struct CssPaletteMixItem(CssValue Palette, CssValue? Percentage);

    /// <summary>
    /// CSS Fonts 4 palette-mix(). Palette resources stay symbolic until the host can resolve them.
    /// Interpolation metadata and percentages are retained even when serialization omits defaults.
    /// </summary>
    public sealed class CssPaletteMixValue : CssValue
    {
        public CssPaletteMixValue(string space, string hueMethod, IReadOnlyList<CssPaletteMixItem> items)
        { Space = space; HueMethod = hueMethod; Items = items; }
        public string Space { get; }
        public string HueMethod { get; }
        public IReadOnlyList<CssPaletteMixItem> Items { get; }

        public override void AppendTo(StringBuilder sb)
        {
            sb.Append("palette-mix(");
            if (Space != "oklab")
            {
                sb.Append("in ").Append(Space);
                if (HueMethod != "shorter") sb.Append(' ').Append(HueMethod).Append(" hue");
                sb.Append(", ");
            }
            double[]? rented = Items.Count > 128 ? ArrayPool<double>.Shared.Rent(Items.Count) : null;
            Span<double> percentages = rented == null ? stackalloc double[Items.Count] : rented.AsSpan(0, Items.Count);
            try
            {
                bool calculational = false;
                double specified = 0;
                int omitted = 0;
                for (int i = 0; i < Items.Count; i++)
                {
                    CssValue? percentage = Items[i].Percentage;
                    if (percentage == null) omitted++;
                    else if (percentage is CssPercentageValue literal) { percentages[i] = literal.Value; specified += literal.Value; }
                    else calculational = true;
                }
                double remainder = omitted == 0 ? 0 : System.Math.Max(100 - specified, 0) / omitted;
                bool equal = !calculational;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Percentage == null) percentages[i] = remainder;
                    if (!calculational && percentages[i] != 100d / Items.Count) equal = false;
                }
                for (int i = 0; i < Items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Items[i].Palette.AppendTo(sb);
                    if (equal) continue;
                    if (Items[i].Percentage is CssValue percentage)
                    {
                        sb.Append(' ');
                        percentage.AppendTo(sb);
                    }
                    else if (!calculational) sb.Append(' ').Append(NumberFormat.Format(percentages[i])).Append('%');
                }
            }
            finally
            {
                if (rented != null) ArrayPool<double>.Shared.Return(rented);
            }
            sb.Append(')');
        }
    }
}
