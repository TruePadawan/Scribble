using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a non-text stroke on the canvas (lines, arrows, rectangles, ellipses)
/// </summary>
public class DrawStroke : PaintableStroke, IClonable
{
    /// <summary>
    /// The Tool that produced the stroke
    /// </summary>
    public required ToolType ToolType { get; set; }

    /// <summary>
    /// The raw input points that build up the stroke
    /// </summary>
    [JsonIgnore]
    public List<StrokePoint> RawPoints { get; init; } = [];

    /// <summary>
    /// The raw points are the nodes in a polyline
    /// This needs to be persisted so polylines work fine when loading a saved state
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("RawPoints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StrokePoint>? SerializedRawPoints
    {
        get => ToolType is ToolType.Line or ToolType.Arrow ? RawPoints : null;
        init
        {
            if (value != null)
            {
                RawPoints = value;
            }
        }
    }

    [JsonIgnore] public SKPath? StablePath { get; set; }

    public override CanvasElement Clone(bool preserveId = false)
    {
        var clone = new DrawStroke
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            ToolType = ToolType,
            Path = new SKPath(Path),
            ToolOptions = [.. ToolOptions],
            Paint = Paint.Clone(),
            RawPoints = [.. RawPoints],
            StablePath = StablePath != null ? new SKPath(StablePath) : null,
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId,
            Rotation = Rotation,
            TransformMatrix = TransformMatrix
        };
        return clone;
    }
}