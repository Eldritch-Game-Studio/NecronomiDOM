namespace NecronomiDom.Dom;

/// <summary>
/// Captures one document root's input state while a global UI block is active.
/// The engine host owns document discovery, native focus and releasing held input.
/// </summary>
public struct DocumentInputBlock
{
    private HTMLElement? _root;
    private HTMLElement? _focused;
    private bool _wasInert;

    public void Block(Document? document)
    {
        var root = document?.DocumentElement as HTMLElement;
        if (!ReferenceEquals(root, _root))
        {
            Restore(document);
            _root = root;
            if (root != null)
            {
                _wasInert = root.Inert;
                _focused = document!.FocusedElement;
            }
        }
        if (root != null && !root.Inert) root.Inert = true;
    }

    public void Restore(Document? currentDocument)
    {
        if (_root == null) return;
        _root.Inert = _wasInert;
        if (ReferenceEquals(currentDocument, _root.OwnerDocument) && _focused?.IsConnected == true)
            _focused.Focus();
        _root = null;
        _focused = null;
    }
}
