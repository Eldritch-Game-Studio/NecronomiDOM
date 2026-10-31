#nullable enable
using System;
using Godot;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>Opt-in native wheel failure evidence; observing never changes routing, capture or default scrolling.</summary>
    public readonly record struct WheelObservation(string Stage, ulong Frame, MouseButton Button, bool Pressed, float Factor,
        float NativeX, float NativeY, float LocalX, float LocalY, double DeltaX, double DeltaY,
        bool InputEnabled, bool HostFocused, bool WindowFocused, bool NativeGuiPress, bool DomCapture,
        string? HitId, string? HitTag, string? FocusId);
    public event Action<WheelObservation>? WheelObserved;

    private void ObserveWheel(string stage, InputEventMouseButton input, Vector2 local, double dx = 0, double dy = 0)
    {
        var observer = WheelObserved;
        if (observer == null) return;
        var hit = DisplayList.HitTest(local.X, local.Y);
        observer(new(stage, Godot.Engine.GetProcessFrames(), input.ButtonIndex, input.Pressed, input.Factor,
            input.Position.X, input.Position.Y, local.X, local.Y, dx, dy, InputEnabled, HasFocus(), GetWindow().HasFocus(),
            _nativeGuiPress, _documentInput?.HasPointerCaptureOverride == true, hit?.Id, hit?.LocalName, Document?.FocusedElement?.Id));
    }

    private bool HandleScrollWheel(InputEventMouseButton input)
    {
        if (input.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight)) return false;
        double delta = (input.Factor == 0 ? 1 : input.Factor) * 40;
        double dx = input.ButtonIndex == MouseButton.WheelLeft ? -delta : input.ButtonIndex == MouseButton.WheelRight ? delta : 0;
        double dy = input.ButtonIndex == MouseButton.WheelUp ? -delta : input.ButtonIndex == MouseButton.WheelDown ? delta : 0;
        if (input.ShiftPressed && dx == 0) { dx = dy; dy = 0; }
        ObserveWheel("dispatch", input, input.Position, dx, dy);
        _documentInput!.Wheel(input.Position.X, input.Position.Y, dx, dy);
        return true;
    }
}
