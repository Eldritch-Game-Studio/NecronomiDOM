#nullable enable
using System;

namespace NecronomiDom.Runtime;

// The runtime compiles against two Godot C# APIs:
// - GodotSharp (Godot .NET edition): the addon's .cs files build into the game assembly.
// - The GDExtension .NET bindings (standard Godot): native/ builds them with NativeAOT into a GDExtension library.
// Each API ignores the other's registration attributes, so shared code carries both; these no-op definitions stand in
// for the attributes the current API lacks.
#if NECRONOMIDOM_GDEXTENSION
/// <summary>Editor-exported property in GodotSharp; the GDExtension build registers <c>[BindProperty]</c> instead.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal sealed class ExportAttribute : Attribute { }
#else
/// <summary>Registers a class with ClassDB in the GDExtension build; GodotSharp registers scripts itself.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class GodotClassAttribute : Attribute { }

/// <summary>ClassDB property in the GDExtension build; GodotSharp uses <c>[Export]</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal sealed class BindPropertyAttribute : Attribute { public string? Name { get; init; } }

/// <summary>ClassDB method in the GDExtension build; GodotSharp exposes public methods to scripts itself.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class BindMethodAttribute : Attribute { public string? Name { get; init; } }
#endif

/// <summary>Engine buffer conversions. GodotSharp marshals managed arrays; the bindings use Packed*Array types. Calls are
/// identities under GodotSharp and copies into packed arrays in the GDExtension build.</summary>
internal static class Packed
{
#if NECRONOMIDOM_GDEXTENSION
    public static Godot.Collections.PackedByteArray Bytes(ReadOnlySpan<byte> values) => new(values);
    public static Godot.Collections.PackedVector2Array Vector2s(ReadOnlySpan<Godot.Vector2> values) => new(values);
    public static Godot.Collections.PackedVector3Array Vector3s(ReadOnlySpan<Godot.Vector3> values) => new(values);
    public static Godot.Collections.PackedInt32Array Ints(ReadOnlySpan<int> values) => new(values);
    public static Godot.Collections.PackedColorArray Colors(ReadOnlySpan<Godot.Color> values) => new(values);
    public static Godot.Vector2[] ToArray(Godot.Collections.PackedVector2Array values) => values.AsSpan().ToArray();
    public static ReadOnlySpan<byte> Span(Godot.Collections.PackedByteArray values) => values.AsSpan();
    public static byte[] ToBytes(Godot.Collections.PackedByteArray values) => values.AsSpan().ToArray();
    public static bool HasChar(Godot.Font font, int scalar) => font.HasChar(new System.Text.Rune(scalar));
    public static int Unicode(Godot.InputEventKey key) => key.Unicode.Value;
#else
    public static byte[] Bytes(byte[] values) => values;
    public static Godot.Vector2[] Vector2s(Godot.Vector2[] values) => values;
    public static Godot.Vector3[] Vector3s(Godot.Vector3[] values) => values;
    public static int[] Ints(int[] values) => values;
    public static Godot.Color[] Colors(Godot.Color[] values) => values;
    public static Godot.Vector2[] ToArray(Godot.Vector2[] values) => values;
    public static ReadOnlySpan<byte> Span(byte[] values) => values;
    public static byte[] ToBytes(byte[] values) => values;
    public static bool HasChar(Godot.Font font, int scalar) => font.HasChar(scalar);
    public static int Unicode(Godot.InputEventKey key) => (int)key.Unicode;
#endif
    /// <summary>Godot's Mesh.ARRAY_MAX: the number of surface array slots.</summary>
    public const int MeshArrayCount = 13;
}
