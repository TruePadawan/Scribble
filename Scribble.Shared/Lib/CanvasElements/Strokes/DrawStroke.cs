using System.Text.Json.Serialization;
using Avalonia.Skia;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a non-text stroke on the canvas (lines, arrows, rectangles, ellipses)
/// </summary>
public class DrawStroke : PaintableStroke, ICopyable
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

    public CanvasElement Copy()
    {
        return new DrawStroke
        {
            ToolType = ToolType,
            Path = Path.Clone(),
            ToolOptions = ToolOptions,
            Paint = Paint.Clone(),
            RawPoints = [..RawPoints],
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId
        };
    }
}