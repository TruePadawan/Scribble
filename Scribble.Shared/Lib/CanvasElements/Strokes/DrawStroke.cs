using System.Text.Json.Serialization;
using Avalonia.Skia;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a non-text stroke on the canvas (lines, arrows, rectangles, ellipses)
/// </summary>
public class DrawStroke(Guid id) : PaintableStroke(id), IClonable
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

    public override void Dispose()
    {
        base.Dispose();
        StablePath?.Dispose();
        StablePath = null;
    }

    public override CanvasElement Clone(bool preserveId = false)
    {
        var clone = new DrawStroke(preserveId ? Id : Guid.NewGuid())
        {
            ToolType = ToolType,
            Path = new SKPath(Path),
            ToolOptions = [..ToolOptions],
            Paint = Paint.Clone(),
            RawPoints = [..RawPoints],
            StablePath = StablePath != null ? new SKPath(StablePath) : null,
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId
        };
        return clone;
    }
}