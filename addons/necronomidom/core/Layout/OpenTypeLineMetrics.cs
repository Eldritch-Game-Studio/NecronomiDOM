using System;
using System.Buffers.Binary;
using System.IO;

namespace NecronomiDom.Layout;

public readonly record struct FontLineMetrics(double Ascent, double Descent, double LineGap);

/// <summary>
/// Public SFNT horizontal metrics, read once from the mapped font's data (not raster-cache metrics).
/// Linux Chromium rounds typographic ascent/descent independently and borrows one ascent pixel
/// when a rounded-down descent would clip overflow-hidden text. This is the pinned browser's
/// generic FontMetrics::AscentDescentWithHacks policy, not a size-specific height adjustment.
/// Policy source: https://raw.githubusercontent.com/chromium/chromium/155.0.8059.39/third_party/blink/renderer/platform/fonts/font_metrics.cc
/// Reads default horizontal SFNT metrics; hinted VDMX and variation-metric overrides are not implemented.
/// <see cref="CapHeight"/> is OS/2 sCapHeight (version 2+), or 0 when the font does not record it.
/// </summary>
public readonly record struct OpenTypeLineMetrics(int UnitsPerEm, int Ascent, int Descent, int LineGap, int CapHeight = 0)
{
    public static OpenTypeLineMetrics Parse(ReadOnlySpan<byte> data, int faceIndex = 0)
    {
        int directory = 0;
        if (ReadU32(data, 0) == 0x74746366) // OpenType collection: offsets are relative to the complete file.
        {
            uint count = ReadU32(data, 8);
            if (faceIndex < 0 || (uint)faceIndex >= count) throw new ArgumentOutOfRangeException(nameof(faceIndex));
            directory = Offset(ReadU32(data, checked(12 + faceIndex * 4)));
        }
        else if (faceIndex != 0) throw new ArgumentOutOfRangeException(nameof(faceIndex));
        uint signature = ReadU32(data, directory);
        if (signature is not (0x00010000 or 0x4F54544F or 0x74727565))
            throw new NotSupportedException("Mapped font data must be OpenType/SFNT.");
        int countTables = ReadU16(data, checked(directory + 4));
        ReadOnlySpan<byte> head = default, hhea = default, os2 = default;
        for (int i = 0; i < countTables; i++)
        {
            int record = checked(directory + 12 + i * 16);
            uint tag = ReadU32(data, record);
            if (tag is not (0x68656164 or 0x68686561 or 0x4F532F32)) continue;
            int offset = Offset(ReadU32(data, checked(record + 8))), length = Offset(ReadU32(data, checked(record + 12)));
            var table = Slice(data, offset, length);
            if (tag == 0x68656164) head = table;
            else if (tag == 0x68686561) hhea = table;
            else os2 = table;
        }
        int units = ReadU16(head, 18);
        if (units == 0) throw new InvalidDataException("OpenType unitsPerEm must be nonzero.");
        int ascent = ReadI16(hhea, 4), descent = -ReadI16(hhea, 6), gap = ReadI16(hhea, 8);
        // Match the browser's USE_TYPO_METRICS selection; otherwise hhea remains authoritative.
        if (os2.Length >= 74 && ReadU16(os2, 0) != 0xFFFF && (ReadU16(os2, 62) & 0x80) != 0)
        { ascent = ReadI16(os2, 68); descent = -ReadI16(os2, 70); gap = ReadI16(os2, 72); }
        int capHeight = os2.Length >= 90 && ReadU16(os2, 0) is >= 2 and not 0xFFFF ? Math.Max(0, (int)ReadI16(os2, 88)) : 0;
        return new OpenTypeLineMetrics(units, ascent, descent, Math.Max(0, gap), capHeight);
    }

    public FontLineMetrics Resolve(int fontSize)
    {
        if (fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        // Skia metrics are single precision. Preserve that boundary before browser quantization.
        float rawAscent = (float)Ascent / UnitsPerEm * fontSize;
        float rawDescent = (float)Descent / UnitsPerEm * fontSize;
        double ascent = Math.Floor(rawAscent + 0.5f), descent = Math.Floor(rawDescent + 0.5f);
        if (descent < rawDescent && ascent >= 1) { descent++; ascent--; }
        double gap = Math.Floor((float)LineGap / UnitsPerEm * fontSize + 0.5f);
        return new FontLineMetrics(ascent, descent, gap);
    }

    private static int Offset(uint value) => value <= int.MaxValue ? (int)value : throw new InvalidDataException("OpenType offset exceeds the mapped data.");
    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
            throw new InvalidDataException("Truncated OpenType table.");
        return data.Slice(offset, length);
    }
    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(Slice(data, offset, 2));
    private static short ReadI16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16BigEndian(Slice(data, offset, 2));
    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(Slice(data, offset, 4));
}
