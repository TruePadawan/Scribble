namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a non-text stroke on the canvas (lines, arrows, rectangles, ellipses)
/// </summary>
public class DrawStroke : PaintableStroke
{
    /// <summary>
    /// The Tool that produced the stroke
    /// </summary>
    public required ToolType ToolType;

    /// <summary>
    /// The raw input points that build up the stroke
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<StrokePoint> RawPoints { get; init; } = [];
}