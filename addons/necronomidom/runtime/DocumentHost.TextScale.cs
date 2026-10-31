using Godot;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>Every mounted host joins this group, so an application-wide text-size setting can reach all
    /// documents: <c>GetTree().CallGroup(DocumentHost.TextScaleGroup, DocumentHost.MethodName.ApplyTextScale, 1.25f)</c>.</summary>
    public const string TextScaleGroup = "necronomidom_text_scale";
    private Control? _scaleParent;
    private Vector2 _authoredScale;
    private float _textScale = 1f;

    /// <summary>Current text scale; 1 is the authored size.</summary>
    public float TextScale => _textScale;

    private void MountTextScale()
    {
        _authoredScale = Scale;
        AddToGroup(TextScaleGroup);
        if (GetParent() is Control parent)
        {
            _scaleParent = parent;
            parent.Resized += FitTextScaleParent;
        }
        if (_scaleParent != null) FitTextScaleParent();
    }

    /// <summary>Scales the whole document about its origin and lays it out at the parent (or viewport) size divided
    /// by <paramref name="scale"/>, so it still fills its container.</summary>
    public void ApplyTextScale(float scale)
    {
        if (_textScale == scale) return;
        _textScale = scale;
        Scale = _authoredScale * scale;
        if (_scaleParent != null) FitTextScaleParent();
        else if (_fittedViewport != null) FitViewport();
        MarkDirty();
    }

    private void FitTextScaleParent() => Size = _scaleParent!.Size / _textScale;

    private void WithdrawTextScale()
    {
        RemoveFromGroup(TextScaleGroup);
        if (_scaleParent != null)
        {
            _scaleParent.Resized -= FitTextScaleParent;
            _scaleParent = null;
        }
        Scale = _authoredScale;
        _textScale = 1f;
    }
}
