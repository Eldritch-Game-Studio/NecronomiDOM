using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Layout;

namespace NecronomiDom.Runtime;

public sealed partial class AdvancedTextShaper
{
    /// <summary>One raster face plus its precision-opted metric clone and lazily synthesized bold/italic paint faces.</summary>
    private sealed class Face : IDisposable
    {
        public readonly FontFile Font;
        public readonly Godot.Collections.Array<Rid> Fonts;
        public readonly Rid MetricRid;
        private readonly FontFile _metricFont;
        private readonly Rid _paintFont;
        private readonly ulong _borrowedSourceId;
        private FontVariation? _syntheticBold;
        private Rid _boldFont;
        private FontVariation? _syntheticItalic;
        private FontVariation? _syntheticBoldItalic;
        private Rid _italicFont;
        private Rid _boldItalicFont;
        private float? _capHeightEm;
        /// <summary>Synthetic oblique shear for faces without an italic alternate.</summary>
        private const float Slant = 0.35f;
        private static readonly StringName SetHighPrecisionShaping = "set_high_precision_shaping";
        private static readonly bool HasHighPrecisionShaping = ClassDB.ClassHasMethod("FontFile", SetHighPrecisionShaping);
        public Face(FontFile font)
        {
            // Borrow the imported asset, but own raster/metric caches and their native atlas textures.
            _borrowedSourceId = font.GetInstanceId(); // ID only: diagnostics must not extend a borrowed asset's lifetime.
            Font = (FontFile)font.Duplicate();
            try
            {
                Font.AllowSystemFallback = false;
                // Keep authored raster hinting unchanged; only the metric clone opts into precision.
                _metricFont = (FontFile)Font.Duplicate();
                _metricFont.SubpixelPositioning = TextServer.SubpixelPositioning.OneQuarter;
                // Unhinted high-precision shaping is an engine extension absent from stock Godot 4.7; use it when bound.
                if (HasHighPrecisionShaping) _metricFont.Call(SetHighPrecisionShaping, true);
                MetricRid = _metricFont.GetRids()[0];
                Fonts = new Godot.Collections.Array<Rid> { MetricRid };
                _paintFont = Font.GetRids()[0];
            }
            catch { _metricFont?.Dispose(); Font.Dispose(); throw; }
        }
        public Rid Paint(double weight, bool italic)
        {
            // Preserve the existing synthetic-bold outline policy and regular
            // logical advances. Synthetic italic is also a quad/ink-only synthesis.
            bool bold = weight >= 600 && Font.GetFontWeight() < 600;
            if (!bold && !italic) return _paintFont;
            if (italic)
            {
                if (bold && _boldItalicFont.IsValid) return _boldItalicFont;
                if (!bold && _italicFont.IsValid) return _italicFont;
                var variation = new FontVariation
                {
                    BaseFont = Font,
                    VariationEmbolden = bold ? 2.0f / 3.0f : 0,
                    // TextServer maps these components directly to FreeType's
                    // y-up outline matrix, not a canvas Transform2D.
                    VariationTransform = new Transform2D(1, Slant, 0, 1, 0, 0)
                };
                Rid font = variation.GetRids()[0];
                if (bold) { _syntheticBoldItalic = variation; _boldItalicFont = font; }
                else { _syntheticItalic = variation; _italicFont = font; }
                return font;
            }
            if (_boldFont.IsValid) return _boldFont;
            _syntheticBold = new FontVariation { BaseFont = Font, VariationEmbolden = 2.0f / 3.0f };
            return _boldFont = _syntheticBold.GetRids()[0];
        }
        public float ItalicPivotOffset(int size)
        {
            // The shear pivots about half the face's cap height (OS/2 sCapHeight), so
            // synthesized italics lean around the middle of capital letters.
            // Faces that do not record a cap height use 0.7 em.
            float capEm = _capHeightEm ??= CapHeightEm(Font);
            return -Slant * (capEm * size / 2);
        }
        private static float CapHeightEm(FontFile font)
        {
            var metrics = OpenTypeLineMetrics.Parse(font.Data);
            return metrics.CapHeight > 0 ? (float)metrics.CapHeight / metrics.UnitsPerEm : 0.7f;
        }
        internal void CaptureOwnership(List<NativeFontOwnership> result)
        {
            result.Add(NativeFontOwnership.From("owned raster", Font, _paintFont, _borrowedSourceId));
            result.Add(NativeFontOwnership.From("owned metric", _metricFont, MetricRid));
            if (_syntheticBold != null) result.Add(NativeFontOwnership.From("owned bold", _syntheticBold, _boldFont));
            if (_syntheticItalic != null) result.Add(NativeFontOwnership.From("owned italic", _syntheticItalic, _italicFont));
            if (_syntheticBoldItalic != null) result.Add(NativeFontOwnership.From("owned bold italic", _syntheticBoldItalic, _boldItalicFont));
        }
        public void Dispose()
        {
            Fonts.Clear();
            _syntheticBold?.Dispose(); _metricFont.Dispose();
            _syntheticItalic?.Dispose(); _syntheticBoldItalic?.Dispose();
            Font.Dispose();
        }
    }
}
