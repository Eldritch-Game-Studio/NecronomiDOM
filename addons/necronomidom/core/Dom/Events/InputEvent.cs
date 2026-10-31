namespace NecronomiDom.Dom;

public class InputEventInit : UIEventInit
{
    public string? Data { get; init; }
    public string InputType { get; init; } = "";
    public bool IsComposing { get; init; }
}

/// <summary>Input Events: beforeinput is cancelable; input reports the completed user mutation.</summary>
public sealed class InputEvent : UIEvent
{
    public InputEvent(string type, InputEventInit? init = null) : base(type, init)
    { Data = init?.Data; InputType = init?.InputType ?? ""; IsComposing = init?.IsComposing ?? false; }
    public string? Data { get; }
    public string InputType { get; }
    public bool IsComposing { get; }
}
