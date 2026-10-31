#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using NecronomiDom.Layout;
using NecronomiDom.Paint;
#if NECRONOMIDOM_GDEXTENSION
using GodotDictionary = Godot.Collections.GodotDictionary;
#else
using GodotDictionary = Godot.Collections.Dictionary;
#endif

namespace NecronomiDom.Runtime;

/// <summary>HarfBuzz/ICU TextServerAdvanced shapes cached runs; every paint consumer shares retained native positions.</summary>
public sealed partial class AdvancedTextShaper : IInlineTextShaper, INativeFontFamilies, IDisposable
{
    private readonly TextServer _server;
    private readonly IReadOnlyList<string> _fallbackPaths;
    private readonly Face?[] _faces;
    private readonly Func<int, int, bool> _hasGlyph;
    private readonly List<FontFaceRange> _items = new();
    private List<bool>? _trackingScalars;
    private readonly GodotDictionary _trackingFeatures = new() { { "liga", 0 }, { "clig", 0 } };
    private readonly OpenTypeLineMetrics _rawLineMetrics;
    private readonly Dictionary<int, FontLineMetrics> _lineMetrics = new();
    private readonly Dictionary<(string Text, int Size, double Weight, double Tracking, bool Italic), (NativeShapedTextRun Run, Rect2? Ink, TextMetrics Metrics, long LayoutPass)> _runs = new();
    public int ShapedRunCount => _runs.Count;
    public string Backend => _server.GetClass() + ": " + _server.GetName();
    /// <summary>Returns the same borrowed native glyph positions used by fill, effects and ink; never reshapes a cached run.</summary>
    public NativeShapedTextRun GetShapedRun(TextRun run) => Shape(run.ShapingText ?? run.Text, run.FontSize, run.FontWeight, run.LetterSpacing, run.FontItalic).Run;
    public AdvancedTextShaper Resolve(TextRun run) => this;
    /// <summary>A single mapped face; a code point it lacks is refused.</summary>
    public AdvancedTextShaper(FontFile font) : this(font, Array.Empty<string>()) { }
    /// <summary>
    /// A code point <paramref name="font"/> lacks resolves through <paramref name="fallbackPaths"/> in order (the first
    /// face that maps it). Fallback faces load on first lookup.
    /// </summary>
    public AdvancedTextShaper(FontFile font, IReadOnlyList<string> fallbackPaths)
    {
        _rawLineMetrics = OpenTypeLineMetrics.Parse(Packed.Span(font.Data));
        _server = TextServerManager.GetPrimaryInterface();
        if (!_server.IsClass("TextServerAdvanced")) throw new NotSupportedException("DocumentHost requires TextServerAdvanced, got " + _server.GetClass() + ": " + _server.GetName());
        _fallbackPaths = fallbackPaths;
        _faces = new Face?[1 + fallbackPaths.Count];
        _faces[0] = new Face(font);
        _hasGlyph = HasGlyph;
    }
    private Face FaceAt(int index)
    {
        if (_faces[index] is Face loaded) return loaded;
        string path = _fallbackPaths[index - 1];
        // Face duplicates the imported asset; release this wrapper so no binding outlives the resource cache.
        using var font = GD.Load<FontFile>(path) ?? throw new InvalidOperationException("Missing fallback font: " + path);
        return _faces[index] = new Face(font);
    }
    private bool HasGlyph(int face, int scalar) => Packed.HasChar(FaceAt(face).Font, scalar);
    private Face FaceOf(Rid metric)
    {
        foreach (var face in _faces)
            if (face != null && face.MetricRid == metric) return face;
        throw new InvalidOperationException("TextServer returned a glyph font outside the shaped fallback faces.");
    }
    private (NativeShapedTextRun Run, Rect2? Ink, TextMetrics Metrics) Shape(string text, int size, double weight, double tracking, bool italic)
    {
        if (!double.IsFinite(weight) || weight < 1 || weight > 1000)
            throw new NotSupportedException("Native font-weight must be finite and in the CSS 1..1000 range.");
        if (!double.IsFinite(tracking) || tracking < long.MinValue || tracking > long.MaxValue)
            throw new NotSupportedException("Native letter-spacing must be finite and within the TextServer spacing range.");
        bool fractional = tracking != Math.Truncate(tracking);
        var key = (text, size, weight, tracking, italic);
        if (_runs.TryGetValue(key, out var cached))
        {
            if (cached.LayoutPass != _layoutPass) { cached.LayoutPass = _layoutPass; _runs[key] = cached; }
            return (cached.Run, cached.Ink, cached.Metrics);
        }
        _items.Clear();
        int missing = FontItemizer.Itemize(text, _faces.Length, _hasGlyph, _items);
        if (missing != 0)
            throw new NotSupportedException(_faces.Length == 1
                ? $"Mapped font has no glyph for U+{missing:X4} and no fallback fonts are configured."
                : $"No font in the fallback chain has a glyph for U+{missing:X4}.");
        if (_items.Count == 0) _items.Add(new FontFaceRange(0, 0, 0));
        if (italic)
            foreach (var item in _items) FaceAt(item.Face).ItalicPivotOffset(size);
        // Resolve bidi across the whole run, not independently per fallback span.
        // Host admission remains LTR-only; direct retained consumers can also use
        // the face's bidi controls without forcing an LTR paragraph.
        Rid rid = _server.CreateShapedText(TextServer.Direction.Auto, TextServer.Orientation.Horizontal);
        NativePositionedGlyph[]? positions = null;
        try
        {
            foreach (var item in _items)
            {
                string span = item.Start == 0 && item.End == text.Length ? text : text[item.Start..item.End];
                var fonts = FaceAt(item.Face).Fonts;
                bool added = tracking == 0
                    ? _server.ShapedTextAddString(rid, span, fonts, size)
                    : _server.ShapedTextAddString(rid, span, fonts, size, _trackingFeatures);
                if (!added) throw new InvalidOperationException("TextServer rejected text.");
            }
            if (!_server.ShapedTextShape(rid)) throw new InvalidOperationException("TextServerAdvanced shaping failed.");
            if (!_lineMetrics.TryGetValue(size, out var lines))
            {
                lines = _rawLineMetrics.Resolve(size);
                _lineMetrics.Add(size, lines);
            }
            // ShapedTextGetWidth ceils even an unrounded run. Layout must use
            // the same retained glyph advance sum as paint, including terminal
            // CSS tracking, rather than that pixel-enclosing convenience width.
            var retained = RetainGlyphs(rid, text, tracking, weight, italic, size);
            positions = retained.Glyphs;
            double width = retained.Width;
            var metrics = new TextMetrics(width, lines.Ascent, lines.Descent) { LineGap = lines.LineGap };
            var shaped = (Run: new NativeShapedTextRun(_server, rid, positions, retained.Count, fractional), Ink: (Rect2?)null, Metrics: metrics);
            _runs.Add(key, (shaped.Run, shaped.Ink, shaped.Metrics, _layoutPass));
            return shaped;
        }
        catch
        {
            if (positions != null) System.Buffers.ArrayPool<NativePositionedGlyph>.Shared.Return(positions);
            _server.FreeRid(rid); throw;
        }
    }


    /// <summary>Cached actual fill texture ink, translated to the same document baseline as Draw.</summary>
    public Rect2 InkBounds(TextRun run)
    {
        if (run.Color.A == 0) return default;
        string context = run.ShapingText ?? run.Text;
        var cached = Shape(context, run.FontSize, run.FontWeight, run.LetterSpacing, run.FontItalic);
        Rect2 ink;
        if (cached.Ink is Rect2 ready) ink = ready;
        else
        {
            ink = cached.Run.Ink().Fill;
            _runs[(context, run.FontSize, run.FontWeight, run.LetterSpacing, run.FontItalic)] = (cached.Run, ink, cached.Metrics, _layoutPass);
        }
        return !ink.HasArea() ? default :
            new Rect2(ink.Position + new Vector2((float)run.X, (float)run.Baseline), ink.Size);
    }
    public TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false) =>
        Shape(text, fontSize, fontWeight, letterSpacing, italic).Metrics;
    public void Draw(Rid canvas, TextRun run) => GetShapedRun(run).Draw(canvas,
        new Vector2((float)run.X, (float)run.Baseline), new Color(run.Color.R, run.Color.G, run.Color.B, run.Color.A));
    public void Clear()
    {
        foreach (var run in _runs.Values)
        { _server.FreeRid(run.Run.ShapedRid); run.Run.ReturnGlyphBuffer(); }
        _runs.Clear();
        _unusedRuns.Clear(); _layoutPass = 0;
    }
    internal void CaptureOwnership(List<NativeFontOwnership> result)
    { foreach (var face in _faces) face?.CaptureOwnership(result); }
    public void Dispose()
    {
        Clear(); _trackingFeatures.Dispose();
        foreach (var face in _faces) face?.Dispose();
    }

}
