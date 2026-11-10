#if !NECRONOMIDOM_GDEXTENSION
#nullable enable
using Godot;

namespace NecronomiDom.Runtime;

public partial class DocumentHost : ISerializationListener
{
    // Godot serializes compatible private fields, but not the DOM, native paint stores
    // or readonly paint owner. A mounted flag cannot survive without those resources.
    private bool _resumeAfterAssemblyReload;

    void ISerializationListener.OnBeforeSerialize()
    {
        _resumeAfterAssemblyReload = _readyInitialized;
        _ExitTree();
        _readyInitialized = false;
        if (_paint.GetParent() == this) RemoveChild(_paint);
        _paint.Free();
    }

    void ISerializationListener.OnAfterDeserialize()
    {
        if (!_resumeAfterAssemblyReload) return;
        _resumeAfterAssemblyReload = false;
        // The new managed instance owns a fresh paint node. Recreate the document
        // from its restored path; do not reuse native children from the old assembly.
        if (IsInsideTree()) _Ready();
        else RequestReady();
    }
}
#endif
