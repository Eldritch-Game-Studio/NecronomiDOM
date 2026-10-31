using System;
using System.Collections.Generic;
using Godot;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    // Action strengths normally combine keyboard and joypad sources. Text editing owns
    // keyboard keys, so retain only actual native joypad contributions for its UA repeat.
    private readonly Dictionary<(int Device, bool Motion, int Control), Vector4> _controllerNavigation = new();

    private void ObserveControllerNavigation(InputEvent input)
    {
        (int Device, bool Motion, int Control) source;
        if (input is InputEventJoypadMotion motion) source = (input.Device, true, (int)motion.Axis);
        else if (input is InputEventJoypadButton button) source = (input.Device, false, (int)button.ButtonIndex);
        else return;
        var strength = new Vector4(input.GetActionStrength(NavLeft), input.GetActionStrength(NavRight),
            input.GetActionStrength(NavUp), input.GetActionStrength(NavDown));
        if (strength == Vector4.Zero) _controllerNavigation.Remove(source);
        else _controllerNavigation[source] = strength;
    }

    private Vector2 ReadControllerNavigation()
    {
        var strength = Vector4.Zero;
        foreach (var entry in _controllerNavigation)
        {
            var value = entry.Value;
            strength.X = Math.Max(strength.X, value.X); strength.Y = Math.Max(strength.Y, value.Y);
            strength.Z = Math.Max(strength.Z, value.Z); strength.W = Math.Max(strength.W, value.W);
        }
        return new Vector2(strength.Y - strength.X, strength.W - strength.Z);
    }

    private void NavigationControllerConnectionChanged(long device, bool connected)
    {
        if (connected) return;
        // .NET Dictionary permits removal during enumeration; no snapshot allocation.
        foreach (var entry in _controllerNavigation)
            if (entry.Key.Device == device) _controllerNavigation.Remove(entry.Key);
    }
}
