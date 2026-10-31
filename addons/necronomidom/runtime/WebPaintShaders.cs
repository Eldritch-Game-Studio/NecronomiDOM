#nullable enable
using System;
using Godot;

namespace NecronomiDom.Runtime;

/// <summary>
/// Every web paint shader, loaded on the main thread and strongly held for the process lifetime; never disposed.
/// ResourceCache holds no reference of its own. A dropped <c>GD.Load</c> wrapper would be a shader's last reference
/// whenever its last material is released (each <see cref="DocumentPaint.Clear"/>): a collection there lets the
/// finalizer thread release the cached shader while the next rebuild's <c>GD.Load</c> re-references it, leaving the
/// C# binding with a collected weak handle (godot#112067, "Handle is not initialized" in SwapGCHandleForType) and
/// racing its GCHandle swaps across threads. Holding the wrapper keeps every binding transition on live handles.
/// </summary>
internal static class WebPaintShaders
{
    private const string Root = "res://addons/necronomidom/shaders/";
    private static Shader? _box, _image, _polygonFill, _polygonClip, _ellipticalClip, _glyphShadow, _gradient, _asymmetricBorder, _linearDocument;

    internal static Shader Box => _box ??= Load("box");
    internal static Shader Image => _image ??= Load("image");
    internal static Shader PolygonFill => _polygonFill ??= Load("polygon-fill");
    internal static Shader PolygonClip => _polygonClip ??= Load("polygon-clip");
    internal static Shader EllipticalClip => _ellipticalClip ??= Load("elliptical-clip");
    internal static Shader GlyphShadow => _glyphShadow ??= Load("glyph-shadow");
    internal static Shader Gradient => _gradient ??= Load("gradient");
    internal static Shader AsymmetricBorder => _asymmetricBorder ??= Load("asymmetric-border");
    internal static Shader LinearDocument => _linearDocument ??= Load("linear-document");

    /// <summary>Drops the process-lifetime references when the GDExtension library unloads; the engine then frees the
    /// shaders. GodotSharp builds never call this.</summary>
    internal static void Release()
    {
        foreach (var shader in new[] { _box, _image, _polygonFill, _polygonClip, _ellipticalClip, _glyphShadow, _gradient, _asymmetricBorder, _linearDocument })
            shader?.Dispose();
        _box = _image = _polygonFill = _polygonClip = _ellipticalClip = _glyphShadow = _gradient = _asymmetricBorder = _linearDocument = null;
    }

    private static Shader Load(string name) => GD.Load<Shader>(Root + name + ".gdshader")
        ?? throw new InvalidOperationException("Missing web paint shader: " + Root + name + ".gdshader");
}
