using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Paint resolves the same authored family identity retained by pure layout.</summary>
public interface INativeFontFamilies
{
    AdvancedTextShaper Resolve(TextRun run);
}

/// <summary>Owns one Advanced shaper (including its precision-opted metric clone) per admitted family; every family
/// resolves glyphs its own face lacks through the host's ordered fallback fonts.</summary>
public sealed partial class NativeFontFamilies : INativeFontFamilies, IDisposable
{
    private readonly Dictionary<string, ITextShaper> _metrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AdvancedTextShaper> _shapers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mapped;
    private readonly string[] _fallbacks;
    private Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ITextShaper> Metrics => _metrics;
    public string Backend { get { foreach (var shaper in _shapers.Values) return shaper.Backend; return ""; } }
    public int ShapedRunCount { get { int count = 0; foreach (var shaper in _shapers.Values) count += shaper.ShapedRunCount; return count; } }

    public NativeFontFamilies(string family, string path, Godot.Collections.Dictionary<string, string> mapped, string[] fallbacks)
    {
        _mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapped) _mapped.Add(pair.Key, pair.Value);
        _mapped[family] = path;
        _fallbacks = (string[])fallbacks.Clone();
    }
    private string[] FallbacksFor(string primary) => Array.FindAll(_fallbacks, fallback => fallback != primary);
    public void Refresh(Document document, Action? beforeReplace = null)
    {
        if (FontSourcesUnchanged(document)) return;
        var paths = AuthoredFontFamilies.Resolve(document, _mapped);
        bool changed = paths.Count != _paths.Count;
        foreach (var pair in paths)
            if (!_paths.TryGetValue(pair.Key, out var old) || old != pair.Value) { changed = true; break; }
        if (!changed) { CaptureFontSources(document); return; }
        var next = new Dictionary<string, AdvancedTextShaper>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var pair in paths)
                next.Add(pair.Key, new AdvancedTextShaper(GD.Load<FontFile>(pair.Value)
                    ?? throw new InvalidOperationException("Missing font: " + pair.Value), FallbacksFor(pair.Value)));
        }
        catch { foreach (var shaper in next.Values) shaper.Dispose(); throw; }
        beforeReplace?.Invoke();
        Dispose();
        foreach (var pair in next) { _shapers.Add(pair.Key, pair.Value); _metrics.Add(pair.Key, pair.Value); }
        _paths = paths;
        CaptureFontSources(document);
    }
    public AdvancedTextShaper Resolve(TextRun run) => _shapers.TryGetValue(run.FontFamily, out var shaper) ? shaper
        : throw new NotSupportedException("Runtime font-family is not authored/mapped: " + run.FontFamily + "; fallback is disabled.");
    public void BeginLayoutPass() { foreach (var shaper in _shapers.Values) shaper.BeginLayoutPass(); }
    internal NativeFontOwnership[] CaptureOwnership()
    {
        var result = new List<NativeFontOwnership>();
        foreach (var shaper in _shapers.Values) shaper.CaptureOwnership(result);
        return result.ToArray();
    }
    public void Dispose()
    {
        foreach (var shaper in _shapers.Values) shaper.Dispose();
        ClearFontSources();
        _shapers.Clear(); _metrics.Clear(); _paths.Clear();
    }
}

internal readonly record struct NativeFontOwnership(string Kind, ulong Id, ulong Rid, long References, ulong BorrowedSourceId)
{
    internal static NativeFontOwnership From(string kind, Font font, Godot.Rid rid, ulong borrowedSourceId = 0) =>
        new(kind, font.GetInstanceId(), rid.Id, font.GetReferenceCount(), borrowedSourceId);
}
