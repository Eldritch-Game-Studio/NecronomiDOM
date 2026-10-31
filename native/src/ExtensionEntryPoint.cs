using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;

[assembly: DisableGodotEntryPointGeneration]

namespace NecronomiDom.Runtime;

/// <summary>
/// GDExtension entry point. <see cref="DocumentHost"/> is the one exposed class; the paint nodes it creates are
/// registered internal, so they stay out of the editor's node list and cannot clash with other extensions' names.
/// </summary>
internal static class ExtensionEntryPoint
{
    private static void Initialize(InitializationLevel level)
    {
        if (level != InitializationLevel.Scene) return;
        GodotRegistry.RegisterRuntimeClass<DocumentHost>(DocumentHost.BindMembers);
        GodotRegistry.RegisterInternalClass<DocumentPaint>(DocumentPaint.BindMembers);
        GodotRegistry.RegisterInternalClass<ShapedTextPaint>(ShapedTextPaint.BindMembers);
        GodotRegistry.RegisterInternalClass<NativeImagePaint>(NativeImagePaint.BindMembers);
        GodotRegistry.RegisterInternalClass<NativeGradientPaint>(NativeGradientPaint.BindMembers);
        GodotRegistry.RegisterInternalClass<NativeGlyphEffectsPaint>(NativeGlyphEffectsPaint.BindMembers);
        GodotRegistry.RegisterInternalClass<NativeGlyphMaskPaint>(NativeGlyphMaskPaint.BindMembers);
        GodotRegistry.RegisterInternalClass<NativeGlyphShadowPaint>(NativeGlyphShadowPaint.BindMembers);
        GodotRegistry.RegisterInternalClass<NativeGlyphBlurTarget>(NativeGlyphBlurTarget.BindMembers);
        GodotRegistry.RegisterInternalClass<NativeGlyphBlurPass>(NativeGlyphBlurPass.BindMembers);
        GodotRegistry.RegisterInternalClass<PolygonPaint>(PolygonPaint.BindMembers);
        // Base before derived.
        GodotRegistry.RegisterInternalClass<PolygonComposite>(PolygonComposite.BindMembers);
        GodotRegistry.RegisterInternalClass<PolygonClipGroup>(PolygonClipGroup.BindMembers);
        GodotRegistry.RegisterInternalClass<RoundedRectClipGroup>(RoundedRectClipGroup.BindMembers);
        GodotRegistry.RegisterInternalClass<PolygonOpacityGroup>(PolygonOpacityGroup.BindMembers);
        GodotRegistry.RegisterInternalClass<EllipticalClipGroup>(EllipticalClipGroup.BindMembers);
        GodotRegistry.RegisterInternalClass<LinearDocumentComposite>(LinearDocumentComposite.BindMembers);
    }

    private static void Deinitialize(InitializationLevel level)
    {
        if (level == InitializationLevel.Scene) WebPaintShaders.Release();
    }

    [UnmanagedCallersOnly(EntryPoint = "necronomidom_init")]
    private static bool Init(nint getProcAddress, nint library, nint initialization)
    {
        GodotBridge.Initialize(getProcAddress, library, initialization, config =>
        {
            config.SetMinimumLibraryInitializationLevel(InitializationLevel.Scene);
            config.RegisterInitializer(Initialize);
            config.RegisterTerminator(Deinitialize);
        });
        return true;
    }
}
