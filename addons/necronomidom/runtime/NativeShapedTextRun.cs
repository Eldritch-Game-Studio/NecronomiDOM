#nullable enable
using System;
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>Actual TextServerAdvanced glyph data, retained once by the owning shaper.</summary>
public readonly record struct NativePositionedGlyph(Rid Font, int Size, long Index, int Start, int End,
    int Count, int Repeat, TextServer.GraphemeFlag Flags, Vector2 Position, double Advance);

/// <summary>Borrowed shaped run and positions; both remain valid until the owning shaper clears or disposes.
/// All spacing uses the same retained native positions and authored raster font,
/// not a second shaper; synthetic bold changes paint ink without widening layout.</summary>
public readonly record struct NativeShapedTextRun
{
    public TextServer Server { get; }
    public Rid ShapedRid { get; }
    public bool FractionalTracking { get; }
    public bool PreserveHexCodes { get; }
    private readonly NativePositionedGlyph[] _glyphs;
    private readonly int _glyphCount;
    public ReadOnlySpan<NativePositionedGlyph> Glyphs => _glyphs.AsSpan(0, _glyphCount);

    internal NativeShapedTextRun(TextServer server, Rid rid, NativePositionedGlyph[] glyphs, int glyphCount, bool fractional)
    {
        Server = server; ShapedRid = rid; _glyphs = glyphs; _glyphCount = glyphCount; FractionalTracking = fractional;
        PreserveHexCodes = server.ShapedTextGetPreserveControl(rid) || server.ShapedTextGetPreserveInvalid(rid);
    }
    internal void ReturnGlyphBuffer() => System.Buffers.ArrayPool<NativePositionedGlyph>.Shared.Return(_glyphs);

    public void Draw(Rid canvas, Vector2 baseline, Color color, long outline = 0)
    {
        foreach (ref readonly var glyph in Glyphs)
        {
            for (int repeat = 0; repeat < glyph.Repeat; repeat++)
            {
                Vector2 position = baseline + glyph.Position + new Vector2((float)(repeat * glyph.Advance), 0);
                if (glyph.Font.IsValid)
                {
                    if (outline == 0) Server.FontDrawGlyph(glyph.Font, canvas, glyph.Size, position, glyph.Index, color);
                    else Server.FontDrawGlyphOutline(glyph.Font, canvas, glyph.Size, outline, position, glyph.Index, color);
                }
                else if (outline == 0 && PreserveHexCodes &&
                    (glyph.Flags & (TextServer.GraphemeFlag.Virtual | TextServer.GraphemeFlag.EmbeddedObject)) == 0)
                    Server.DrawHexCodeBox(canvas, glyph.Size, position, glyph.Index, color);
            }
        }
    }

    internal (Rect2 Fill, Rect2 Stroke) Ink(long outline = 0, bool requireScalableFont = false)
    {
        Rect2 fillBounds = default, strokeBounds = default;
        Rid checkedFont = default;
        foreach (ref readonly var glyph in Glyphs)
        {
            Rect2 fill = default, stroke = default;
            if (glyph.Font.IsValid)
            {
                if (requireScalableFont && glyph.Font != checkedFont)
                {
                    if (Server.FontIsMultichannelSignedDistanceField(glyph.Font) || Server.FontGetFixedSize(glyph.Font) > 0)
                        throw new NotSupportedException("Native glyph effects require the mapped scalable raster font, not MSDF or fixed-size bitmap glyphs.");
                    checkedFont = glyph.Font;
                }
                var cache = new Vector2I(glyph.Size, 0);
                Server.FontRenderGlyph(glyph.Font, cache, glyph.Index);
                fill = new Rect2(glyph.Position + Server.FontGetGlyphOffset(glyph.Font, cache, glyph.Index),
                    Server.FontGetGlyphSize(glyph.Font, cache, glyph.Index));
                if (outline > 0)
                {
                    cache.Y = checked((int)outline);
                    Server.FontRenderGlyph(glyph.Font, cache, glyph.Index);
                    stroke = new Rect2(glyph.Position + Server.FontGetGlyphOffset(glyph.Font, cache, glyph.Index),
                        Server.FontGetGlyphSize(glyph.Font, cache, glyph.Index));
                }
            }
            else if (glyph.Index != 0)
            {
                if (requireScalableFont) throw new NotSupportedException("Native glyph effects require the mapped font to contain every shaped glyph.");
                if (PreserveHexCodes && (glyph.Flags & (TextServer.GraphemeFlag.Virtual | TextServer.GraphemeFlag.EmbeddedObject)) == 0)
                {
                    Vector2 extent = Server.GetHexCodeBoxSize(glyph.Size, glyph.Index);
                    fill = new Rect2(glyph.Position - new Vector2(0, (int)(extent.Y * .85)), extent);
                }
            }
            for (int repeat = 0; repeat < glyph.Repeat; repeat++)
            {
                Vector2 offset = new((float)(repeat * glyph.Advance), 0);
                if (fill.HasArea()) fillBounds = Merge(fillBounds, new Rect2(fill.Position + offset, fill.Size));
                if (stroke.HasArea()) strokeBounds = Merge(strokeBounds, new Rect2(stroke.Position + offset, stroke.Size));
            }
        }
        return (fillBounds, strokeBounds);
    }
    private static Rect2 Merge(Rect2 first, Rect2 second) => !first.HasArea() ? second : !second.HasArea() ? first : first.Merge(second);
}
