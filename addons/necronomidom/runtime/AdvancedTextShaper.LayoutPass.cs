using System.Collections.Generic;

namespace NecronomiDom.Runtime;

public sealed partial class AdvancedTextShaper
{
    private long _layoutPass;
    private readonly List<(string Text, int Size, double Weight, double Tracking, bool Italic)> _unusedRuns = new();

    /// <summary>Starts a layout after all previous paint consumers have released their borrowed runs.
    /// Retains the preceding pass for reuse; older unused runs are freed, bounding changing text to two passes.
    /// Explicit Clear/Dispose still release every run immediately.</summary>
    public void BeginLayoutPass()
    {
        _layoutPass++;
        foreach (var pair in _runs)
            if (pair.Value.LayoutPass < _layoutPass - 1) _unusedRuns.Add(pair.Key);
        foreach (var key in _unusedRuns)
        {
            _server.FreeRid(_runs[key].Run.ShapedRid);
            _runs[key].Run.ReturnGlyphBuffer();
            _runs.Remove(key);
        }
        _unusedRuns.Clear();
    }
}
