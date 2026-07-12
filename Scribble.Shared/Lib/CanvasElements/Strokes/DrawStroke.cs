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
    public required ToolType ToolType;

    /// <summary>
    /// The raw input points that build up the stroke
    /// </summary>
    [JsonIgnore]
    public List<StrokePoint> RawPoints { get; init; } = [];

    [JsonIgnore] public SKPath? StablePath { get; set; }

    public override CanvasElement Clone(bool preserveId = false)
    {
        var clone = new DrawStroke
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            ToolType = ToolType,
            Path = new SKPath(Path),
            ToolOptions = [..ToolOptions],
            Paint = Paint.Clone(),
            RawPoints = [..RawPoints],
            StablePath = StablePath != null ? new SKPath(StablePath) : null,
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId,
            Rotation = Rotation
        };
        return clone;
    }
}