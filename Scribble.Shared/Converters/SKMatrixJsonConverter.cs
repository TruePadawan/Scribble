using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Converters;

public class SKMatrixJsonConverter : JsonConverter<SKMatrix>
{
    public override SKMatrix Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected array to deserialize SKMatrix.");
        }

        var values = new float[9];
        int index = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if (index >= 9)
            {
                throw new JsonException("SKMatrix array cannot exceed 9 elements.");
            }

            values[index++] = reader.GetSingle();
        }

        if (index != 9)
        {
            throw new JsonException("SKMatrix array must contain exactly 9 elements.");
        }

        return new SKMatrix(values);
    }

    public override void Write(Utf8JsonWriter writer, SKMatrix value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        var values = value.Values;
        for (int i = 0; i < 9; i++)
        {
            writer.WriteNumberValue(values[i]);
        }
        writer.WriteEndArray();
    }
}
