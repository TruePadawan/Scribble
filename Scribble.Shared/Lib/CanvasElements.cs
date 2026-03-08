using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Lib;

[JsonDerivedType(typeof(CanvasImage), typeDiscriminator: "CanvasImage")]
public abstract class CanvasElement
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

[JsonDerivedType(typeof(DrawStroke), typeDiscriminator: "DrawStroke")]
[JsonDerivedType(typeof(EraserStroke), typeDiscriminator: "EraserStroke")]
[JsonDerivedType(typeof(SelectionBound), typeDiscriminator: "SelectionBound")]
public abstract class Stroke : CanvasElement
{
    [JsonConverter(typeof(SKPathJsonConverter))]
    public required SKPath Path { get; init; } = new();
}

public class DrawStroke : Stroke
{
    public bool IsToBeErased = false;
    public required ToolType ToolType;
    public required HashSet<ToolOption> ToolOptions { get; init; } = [];
    public required StrokePaint Paint { get; init; } = new();
}

public class EraserStroke : Stroke
{
    public HashSet<Guid> Targets = [];
}

public class SelectionBound : Stroke
{
    public HashSet<Guid> Targets = [];
}

public class CanvasImage : CanvasElement
{
    public required string ImageBase64String { get; init; }
    public required SKRect Bounds { get; init; }
    public bool IsToBeErased = false;
}

public enum ToolType
{
    Pencil,
    Line,
    Arrow,
    Ellipse,
    Rectangle,
    Text
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