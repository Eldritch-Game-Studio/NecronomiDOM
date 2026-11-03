namespace NecronomiDom.Dom;

/// <summary>On-demand state only; observing does not dispatch pointer events or change capture/focus.</summary>
public readonly record struct DocumentInputInspection(Element? Hovered, Element? Pressed,
    EventTarget? Captured, Element? PendingCapture, bool PointerDown);

public sealed partial class DocumentInput
{
    public DocumentInputInspection InspectState() =>
        new(_hover, _pressed, _captureTarget, _pendingCaptureTarget, _pointerDown);
}
