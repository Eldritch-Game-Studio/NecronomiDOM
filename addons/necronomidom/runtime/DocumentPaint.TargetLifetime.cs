#nullable enable
using Godot;

namespace NecronomiDom.Runtime;

public partial class DocumentPaint
{
    // A hidden document, or one that paints nothing (for example a root toggled to display:none), keeps no
    // device-sized offscreen targets. Each composite drops to its minimum size but keeps its node and viewport,
    // so showing the document again needs no viewport creation: the targets regrow and recapture in the same
    // frame, before the parent canvas draws them.
    private bool _targetsReleased;

    private bool TargetsHidden => IsInsideTree() && !IsVisibleInTree();

    private void ReleaseTargets()
    {
        _linearDocument?.ReleaseTarget();
        foreach (var context in _isolatedContexts) context.ReleaseTarget();
        _polygons.ReleaseSpareTargets();
        _targetsReleased = true;
    }

    private void RestoreTargets()
    {
        if (!_targetsReleased) return;
        _targetsReleased = false;
        foreach (var context in _isolatedContexts) context.BeginRebuild();
        foreach (var context in _isolatedContexts) context.MeasurePaint();
        foreach (var context in _isolatedContexts) context.FitTarget(true);
    }
}
