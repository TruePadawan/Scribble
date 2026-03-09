using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Converters;

public class SKColorJsonConverter : JsonConverter<SKColor>
{
    public override SKColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hexString = reader.GetString();
        return SKColor.Parse(hexString);
    }

    public override void Write(Utf8JsonWriter writer, SKColor value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}