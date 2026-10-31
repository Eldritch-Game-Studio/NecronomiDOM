namespace NecronomiDom.Dom;

public class UIEventInit : EventInit { public int Detail { get; init; } }
public class UIEvent : Event
{
    public UIEvent(string type, UIEventInit? init = null) : base(type, init) => Detail = init?.Detail ?? 0;
    public int Detail { get; }
}
public class FocusEventInit : UIEventInit { public EventTarget? RelatedTarget { get; init; } }
public sealed class FocusEvent : UIEvent
{
    public FocusEvent(string type, FocusEventInit? init = null) : base(type, init) => RelatedTargetCore = init?.RelatedTarget;
    public EventTarget? RelatedTarget => RelatedTargetCore;
}
public class KeyboardEventInit : UIEventInit
{
    public string Key { get; init; } = "";
    public string Code { get; init; } = "";
    public bool Repeat { get; init; }
    public bool ShiftKey { get; init; }
    public bool CtrlKey { get; init; }
    public bool AltKey { get; init; }
    public bool MetaKey { get; init; }
}
public sealed class KeyboardEvent : UIEvent
{
    public KeyboardEvent(string type, KeyboardEventInit? init = null) : base(type, init)
    { Key = init?.Key ?? ""; Code = init?.Code ?? ""; Repeat = init?.Repeat ?? false; ShiftKey = init?.ShiftKey ?? false; CtrlKey = init?.CtrlKey ?? false; AltKey = init?.AltKey ?? false; MetaKey = init?.MetaKey ?? false; }
    public string Key { get; }
    public string Code { get; }
    public bool Repeat { get; }
    public bool ShiftKey { get; }
    public bool CtrlKey { get; }
    public bool AltKey { get; }
    public bool MetaKey { get; }
}
public class MouseEventInit : UIEventInit
{
    public double ClientX { get; init; }
    public double ClientY { get; init; }
    public int Button { get; init; }
    public int Buttons { get; init; }
    public EventTarget? RelatedTarget { get; init; }
}
public class MouseEvent : UIEvent
{
    public MouseEvent(string type, MouseEventInit? init = null) : base(type, init)
    { ClientX = init?.ClientX ?? 0; ClientY = init?.ClientY ?? 0; Button = init?.Button ?? 0; Buttons = init?.Buttons ?? 0; RelatedTargetCore = init?.RelatedTarget; }
    public double ClientX { get; }
    public double ClientY { get; }
    public int Button { get; }
    public int Buttons { get; }
    public EventTarget? RelatedTarget => RelatedTargetCore;
    protected internal override bool IsActivationEvent => Type == "click";
}
public class PointerEventInit : MouseEventInit
{
    public int PointerId { get; init; }
    public string PointerType { get; init; } = "";
    public bool IsPrimary { get; init; }
}
public sealed class PointerEvent : MouseEvent
{
    public PointerEvent(string type, PointerEventInit? init = null) : base(type, init)
    { PointerId = init?.PointerId ?? 0; PointerType = init?.PointerType ?? ""; IsPrimary = init?.IsPrimary ?? false; }
    public int PointerId { get; }
    public string PointerType { get; }
    public bool IsPrimary { get; }
}
public sealed class NavigationEvent : Event
{
    public NavigationEvent(string type, string direction, EventTarget? relatedTarget = null)
        : base(type, new EventInit { Bubbles = true, Cancelable = true }) { Dir = direction; RelatedTargetCore = relatedTarget; }
    public string Dir { get; }
    public EventTarget? RelatedTarget => RelatedTargetCore;
}
