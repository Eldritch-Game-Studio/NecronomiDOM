#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

namespace NecronomiDom.Runtime;

public sealed partial class AdvancedTextShaper
{
    private (NativePositionedGlyph[] Glyphs, int Count, double Width) RetainGlyphs(Rid shaped, string text, double tracking, double weight, bool italic, int size)
    {
        // Native control glyphs may use the space glyph's nonzero index (RLM,
        // RLO/PDF), so glyph identity alone cannot decide whether CSS tracks them.
        // Cache scalar eligibility in reusable scratch storage: visual bidi order
        // is not UTF-16 order, and a cluster may contain both text and controls.
        if (tracking != 0)
        {
            _trackingScalars ??= new List<bool>(text.Length);
            _trackingScalars.Clear();
            foreach (var rune in text.EnumerateRunes())
                _trackingScalars.Add(Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format);
        }
        var glyphs = _server.ShapedTextGetGlyphs(shaped);
        NativePositionedGlyph[]? positions = null;
        try
        {
            positions = System.Buffers.ArrayPool<NativePositionedGlyph>.Shared.Rent(glyphs.Count);
            double advance = 0;
            int clusterEnd = 0;
            bool clusterTracks = false;
            for (int i = 0; i < glyphs.Count; i++)
            {
                using var glyph = glyphs[i];
                int count = glyph["count"].AsInt32();
                if (count > 0) { clusterEnd = i + count; clusterTracks = false; }
                double step = glyph["advance"].AsDouble();
                long index = glyph["index"].AsInt64();
                int repeat = glyph["repeat"].AsInt32();
                int start = glyph["start"].AsInt32(), end = glyph["end"].AsInt32();
                // Keep tracking standalone marks, but not invisible formatting
                // controls, even when TextServer represents them with a space RID.
                if (tracking != 0 && (index != 0 || step != 0))
                    for (int scalar = start; scalar < end; scalar++)
                        if (_trackingScalars![scalar]) { clusterTracks = true; break; }
                // Marks keep their native offsets within the cluster. Tracking
                // advances the next cluster, not an arbitrary UTF-16 code unit.
                double spacing = i + 1 == clusterEnd && clusterTracks ? tracking : 0;
                Vector2 offset = glyph["offset"].AsVector2();
                Rid metric = glyph["font_rid"].AsRid();
                Rid paintFont = default;
                float italicOffset = 0;
                if (metric.IsValid)
                {
                    // Each face keeps its own synthesis: a fallback glyph is styled
                    // with that fallback face's own metrics.
                    var face = FaceOf(metric);
                    paintFont = face.Paint(weight, italic);
                    if (italic) italicOffset = face.ItalicPivotOffset(size);
                }
                positions[i] = new NativePositionedGlyph(paintFont,
                    glyph["font_size"].AsInt32(), index,
                    start, end, count, repeat,
                    (TextServer.GraphemeFlag)glyph["flags"].AsInt64(),
                    offset + new Vector2((float)advance + italicOffset, 0), step + spacing);
                for (int j = 0; j < repeat; j++) advance += step + spacing;
            }
            return (positions, glyphs.Count, advance);
        }
        catch
        {
            if (positions != null) System.Buffers.ArrayPool<NativePositionedGlyph>.Shared.Return(positions);
            throw;
        }
        finally { glyphs.Clear(); }
    }
}
