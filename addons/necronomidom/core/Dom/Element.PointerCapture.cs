namespace NecronomiDom.Dom;

public partial class Element
{
    /// <summary>Pointer Events § 9: set the pending capture override for an active pointer.</summary>
    public void SetPointerCapture(int pointerId) => ActivePointerInput.SetPointerCapture(this, pointerId);
    public void ReleasePointerCapture(int pointerId) => ActivePointerInput.ReleasePointerCapture(this, pointerId);
    public bool HasPointerCapture(int pointerId) => NodeDocument.PointerInput?.HasPointerCapture(this, pointerId) ?? false;
    private DocumentInput ActivePointerInput => NodeDocument.PointerInput ?? throw DomException.NotFound("No active pointer with the supplied pointerId.");
}

public partial class Document
{
    // The document's real native input controller owns active pointers; dispatchEvent does not create them.
    internal DocumentInput? PointerInput { get; set; }
}
