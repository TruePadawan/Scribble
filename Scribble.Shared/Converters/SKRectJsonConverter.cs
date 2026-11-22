using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Converters;

/// <summary>Serializes image bounds without traversing Skia's computed rectangles.</summary>
public sealed class SKRectJsonConverter : JsonConverter<SKRect>
{
    /// <summary>Reads the four edges in left, top, right, bottom order.</summary>
    public override SKRect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var edges = JsonSerializer.Deserialize<float[]>(ref reader);
        return edges is { Length: 4 }
            ? new SKRect(edges[0], edges[1], edges[2], edges[3])
            : throw new JsonException("Expected four image bounds coordinates.");
    }

    /// <summary>Writes only the rectangle's coordinates.</summary>
    public override void Write(Utf8JsonWriter writer, SKRect value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new[] { value.Left, value.Top, value.Right, value.Bottom });
}