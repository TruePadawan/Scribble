using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Converters;

/// <summary>
/// Converts an SKPath to and from an SVG path string
/// This preserves Moves, Lines, Curves, and Shapes automatically
/// </summary>
public class SKPathJsonConverter : JsonConverter<SKPath>
{
    public override SKPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? svgPathData = reader.GetString();

        if (string.IsNullOrEmpty(svgPathData))
        {
            return new SKPath();
        }

        return SKPath.ParseSvgPathData(svgPathData);
    }

    public override void Write(Utf8JsonWriter writer, SKPath value, JsonSerializerOptions options)
    {
        string svgPathData = value.ToSvgPathData();
        writer.WriteStringValue(svgPathData);
    }
}