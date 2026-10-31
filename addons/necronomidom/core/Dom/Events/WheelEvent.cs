namespace NecronomiDom.Dom;

public sealed class WheelEventInit : MouseEventInit
{
    public double DeltaX { get; init; }
    public double DeltaY { get; init; }
    public uint DeltaMode { get; init; }
}

public sealed class WheelEvent : MouseEvent
{
    public WheelEvent(string type, WheelEventInit? init = null) : base(type, init)
    { DeltaX = init?.DeltaX ?? 0; DeltaY = init?.DeltaY ?? 0; DeltaMode = init?.DeltaMode ?? 0; }
    public double DeltaX { get; }
    public double DeltaY { get; }
    public uint DeltaMode { get; }
}
