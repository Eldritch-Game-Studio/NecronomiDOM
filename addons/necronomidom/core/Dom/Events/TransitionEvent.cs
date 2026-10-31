namespace NecronomiDom.Dom;

public sealed class TransitionEventInit : EventInit
{
    public string PropertyName { get; init; } = "";
    public double ElapsedTime { get; init; }
    public string PseudoElement { get; init; } = "";
}

/// <summary>CSS Transitions §6: elapsed active time is seconds and excludes positive delay.</summary>
public sealed class TransitionEvent : Event
{
    public TransitionEvent(string type, TransitionEventInit? init = null) : base(type, init)
    {
        PropertyName = init?.PropertyName ?? "";
        ElapsedTime = init?.ElapsedTime ?? 0;
        PseudoElement = init?.PseudoElement ?? "";
    }
    public string PropertyName { get; }
    public double ElapsedTime { get; }
    public string PseudoElement { get; }
}
