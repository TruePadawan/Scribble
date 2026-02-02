using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Lib;

[JsonDerivedType(typeof(DrawStroke), typeDiscriminator: "DrawStroke")]
[JsonDerivedType(typeof(EraserStroke), typeDiscriminator: "EraserStroke")]
[JsonDerivedType(typeof(SelectionBound), typeDiscriminator: "SelectionBound")]
public abstract class Stroke
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [JsonConverter(typeof(SKPathJsonConverter))]
    public SKPath Path { get; init; } = new();
}

public enum StrokeTool
{
    Pencil,
    Line,
    Arrow,
    Ellipse,
    Rectangle,
    Text
}

public class DrawStroke : Stroke
{
    public bool IsToBeErased = false;
    public StrokeTool ToolType;
    public StrokePaint Paint { get; init; } = new();
}

public class EraserStroke : Stroke
{
    public HashSet<Guid> Targets = [];
}

public class SelectionBound : Stroke
{
    public HashSet<Guid> Targets = [];
}

public enum StrokeStyle
{
    Solid,
    Dash,
    Dotted
}

public enum EdgeType
{
    Sharp,
    Rounded
}

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
        // Convert the Path to an SVG path string
        // This preserves Moves, Lines, Curves, and Shapes automatically
        string svgPathData = value.ToSvgPathData();
        writer.WriteStringValue(svgPathData);
    }
}