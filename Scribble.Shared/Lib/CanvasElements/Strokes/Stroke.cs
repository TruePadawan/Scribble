using System.Text.Json.Serialization;
using Scribble.Shared.Converters;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

[JsonDerivedType(typeof(DrawStroke), typeDiscriminator: "DrawStroke")]
[JsonDerivedType(typeof(EraserStroke), typeDiscriminator: "EraserStroke")]
[JsonDerivedType(typeof(SelectionBound), typeDiscriminator: "SelectionBound")]
public abstract class Stroke : CanvasElement
{
    [JsonConverter(typeof(SKPathJsonConverter))]
    public required SKPath Path { get; init; } = new();
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