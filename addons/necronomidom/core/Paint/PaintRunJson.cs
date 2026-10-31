using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NecronomiDom.Dom;

namespace NecronomiDom.Paint;

/// <summary>Plain-value JSON for paint runs (diagnostics and test snapshots). Runs carry live DOM attribution
/// (<see cref="TextRun.Owner"/>, <see cref="TextRun.TextControl"/>, <see cref="InlineTextFragment.Node"/>)
/// whose object graph is cyclic (Element.Attributes.OwnerElement...); every DOM node is written as
/// its element id (or node name for non-elements) so the output records values, never live objects.</summary>
public static class PaintRunJson
{
    public static readonly JsonSerializerOptions Options = new() { Converters = { new NodeReferenceConverterFactory() } };

    public static JsonElement ToElement(PaintRun run) => JsonSerializer.SerializeToElement(run, run.GetType(), Options);

    private sealed class NodeReferenceConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeof(Node).IsAssignableFrom(typeToConvert);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(NodeReferenceConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class NodeReferenceConverter<T> : JsonConverter<T> where T : Node
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Paint run JSON is write-only.");
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value is Element element ? element.Id : value.NodeName);
    }
}
