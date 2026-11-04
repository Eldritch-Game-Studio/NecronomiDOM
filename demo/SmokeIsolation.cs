using Godot;

namespace NecronomiDomDemo;

/// <summary>Smokes drive documents with synthesized pointer and keyboard events. Any controller attached to the
/// machine, including a virtual one such as a game-streaming pad on a self-hosted runner, is real input to every Godot
/// process there: a held stick or button moves document focus or suspends keyboard input mid-test. Removing joypad
/// bindings from the input map leaves only the smoke's own events able to act.</summary>
internal static class SmokeIsolation
{
    public static void IgnoreHostControllers()
    {
        foreach (StringName action in InputMap.GetActions())
        {
            foreach (InputEvent binding in InputMap.ActionGetEvents(action))
                if (binding is InputEventJoypadButton or InputEventJoypadMotion) InputMap.ActionEraseEvent(action, binding);
            // A controller already held at startup may have pressed the action before its binding was removed.
            Input.ActionRelease(action);
        }
    }
}
