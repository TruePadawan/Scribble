namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a stroke that has a visual paint and can be erased/selected.
/// Base class for both DrawStroke and TextStroke.
/// </summary>
public abstract class PaintableStroke : Stroke, IClonable
{
    public bool IsToBeErased = false;
    public required HashSet<ToolOption> ToolOptions { get; init; } = [];
    public required StrokePaint Paint { get; init; } = new();
    public abstract CanvasElement Clone(bool preserveId = false);
}