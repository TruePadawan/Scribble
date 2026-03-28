namespace Scribble.Shared.Lib.CanvasElements.Strokes;

public class DrawStroke : Stroke
{
    public bool IsToBeErased = false;
    public required ToolType ToolType;
    public required HashSet<ToolOption> ToolOptions { get; init; } = [];
    public required StrokePaint Paint { get; init; } = new();
}