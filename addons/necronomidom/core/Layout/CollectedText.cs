using System;
using System.Text;

namespace NecronomiDom.Layout;

/// <summary>Collects the adjacent DOM text of one element for white-space collapsing. A single text node
/// whose data is already collapsed is handed back as that same string, so steady-state layout of ordinary
/// text leaves ("Master", "-06.0 dB") allocates nothing; several nodes or collapsible runs use one builder.</summary>
internal sealed class CollectedText
{
    private string? _single;
    private readonly StringBuilder _builder = new();
    private int _pieces;

    public int Length => _pieces switch { 0 => 0, 1 => _single!.Length, _ => _builder.Length };
    public void Clear() { _single = null; _pieces = 0; _builder.Clear(); }
    public void Append(string data)
    {
        if (data.Length == 0) return;
        switch (_pieces)
        {
            case 0: _single = data; _pieces = 1; return;
            case 1: _builder.Append(_single).Append(data); _single = null; _pieces = 2; return;
            default: _builder.Append(data); return;
        }
    }
    /// <summary>Same as string.IsNullOrWhiteSpace over the collected text.</summary>
    public bool IsWhiteSpace()
    {
        if (_pieces == 1) return string.IsNullOrWhiteSpace(_single);
        foreach (var chunk in _builder.GetChunks())
            foreach (char c in chunk.Span)
                if (!char.IsWhiteSpace(c)) return false;
        return true;
    }
    private static bool Collapsible(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';
    /// <summary>Trims and collapses runs of document white space (space, tab, LF, CR, FF) to single spaces.</summary>
    public string Collapse()
    {
        if (_pieces == 0) return "";
        if (_pieces == 1)
        {
            string text = _single!;
            if (text.Length == 0) return text;
            bool identity = !Collapsible(text[0]) && !Collapsible(text[^1]);
            for (int i = 1; identity && i < text.Length; i++)
                if (Collapsible(text[i]) && (text[i] != ' ' || Collapsible(text[i - 1]))) identity = false;
            if (identity) return text;
            return Collapse(text.AsSpan());
        }
        bool empty = true;
        foreach (var chunk in _builder.GetChunks())
        {
            foreach (char c in chunk.Span) if (!Collapsible(c)) { empty = false; break; }
            if (!empty) break;
        }
        if (empty) return "";
        var result = new StringBuilder(_builder.Length); bool space = false;
        foreach (var chunk in _builder.GetChunks()) Collapse(chunk.Span, result, ref space);
        return result.ToString();
    }
    private static string Collapse(ReadOnlySpan<char> text)
    {
        var result = new StringBuilder(text.Length); bool space = false;
        Collapse(text, result, ref space);
        return result.ToString();
    }
    private static void Collapse(ReadOnlySpan<char> text, StringBuilder result, ref bool space)
    {
        foreach (char c in text)
        {
            if (Collapsible(c)) { space = result.Length > 0; continue; }
            if (space) result.Append(' ');
            result.Append(c); space = false;
        }
    }
}
