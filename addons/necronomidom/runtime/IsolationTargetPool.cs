#nullable enable
using System.Collections.Generic;
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>A composite's offscreen isolation target: a render-only SubViewport with its own World2D, the content
/// root painted into it, and the viewport's own texture wrapper.</summary>
internal sealed class IsolationTarget
{
    internal static readonly Vector2I MinimumSize = new(2, 2);
    internal readonly World2D World = new();
    internal readonly SubViewport Viewport;
    // This render-only World2D is a separate interpolation scope; UI poses advance per process frame.
    internal readonly Node2D Content = new() { PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off };
    internal readonly ViewportTexture Texture;

    internal IsolationTarget()
    {
        Viewport = new SubViewport
        {
            Size = MinimumSize, TransparentBg = true, Disable3D = true,
            GuiDisableInput = true, World2D = World,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            HandleInputLocally = false
        };
        Viewport.AddChild(Content);
        // The viewport's default texture: no RenderingServer readback.
        Texture = Viewport.GetTexture();
    }

    /// <summary>Detaches the target from its composite and deletes the painted subtree exactly as freeing the composite
    /// would, then restores the construction-time render state a fresh composite assumes.</summary>
    internal void Reset()
    {
        Viewport.GetParent()?.RemoveChild(Viewport);
        for (int i = Content.GetChildCount() - 1; i >= 0; i--)
        {
            Node child = Content.GetChild(i);
            Content.RemoveChild(child);
            child.Free();
        }
        Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (Viewport.Size != MinimumSize) Viewport.Size = MinimumSize;
        if (Viewport.UseHdr2D) Viewport.UseHdr2D = false;
        if (Viewport.CanvasTransform != Transform2D.Identity) Viewport.CanvasTransform = Transform2D.Identity;
    }

    internal void Free()
    {
        Texture.Dispose(); World.Dispose();
        Viewport.Free();
    }
}

/// <summary>
/// Process-wide spare isolation targets. Godot's Viewport constructor makes two synchronous RenderingServer calls
/// (viewport_get_texture and, in the pinned engine, viewport_set_sdf_oversize_and_scale), so every new SubViewport
/// waits for the render thread. A composite therefore takes a released target when one is spare, and a document that
/// closes leaves its targets for the next one that opens. Spares are detached, minimum-size and never drawn. They are
/// kept only while some composite is alive: the last composite's release, including process exit, frees them all.
/// The editor never pools, because an assembly reload would drop the managed references to detached spares.
/// </summary>
internal static class IsolationTargetPool
{
    internal const int Capacity = 32;
    private static readonly Stack<IsolationTarget> Spares = new();
    private static int _live;

    /// <summary>SubViewports constructed so far; reuse leaves it unchanged.</summary>
    internal static int ConstructedCount { get; private set; }
    internal static int SpareCount => Spares.Count;
    internal static bool IsSpare(ulong viewportId)
    {
        foreach (IsolationTarget target in Spares) if (target.Viewport.GetInstanceId() == viewportId) return true;
        return false;
    }

    internal static IsolationTarget Acquire()
    {
        _live++;
        if (Spares.Count > 0) return Spares.Pop();
        ConstructedCount++;
        return new IsolationTarget();
    }

    internal static void Release(IsolationTarget target)
    {
        // Count first: resetting can free nested composites, which release their own targets.
        _live--;
        target.Reset();
        if (_live > 0 && Spares.Count < Capacity && !Engine.IsEditorHint()) { Spares.Push(target); return; }
        target.Free();
        if (_live == 0) while (Spares.Count > 0) Spares.Pop().Free();
    }
}
