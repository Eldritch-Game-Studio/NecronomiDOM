#nullable enable
using NecronomiDom.Layout;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>Immutable color-transition interpolation profile chosen before document resources bind; defaults to CSS.</summary>
    public ForegroundColorTransitionProfile ForegroundColorTransitionProfile
    {
        get => _foregroundColorController.Profile;
        init => _foregroundColorController = new(value);
    }
    public bool HasRunningForegroundColorTransitions => _foregroundColorController.HasRunningTransitions;
    private readonly ForegroundColorTransitionController _foregroundColorController = new();

    private void ReconcileForegroundColorStyles(double now) =>
        _foregroundColorController.ReconcileStyles(Document, Styles, now);
    private void ClearDocumentForegroundColor(double now) => _foregroundColorController.Clear(now);
}
