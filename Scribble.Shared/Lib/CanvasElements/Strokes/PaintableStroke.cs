namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a stroke that has a visual paint and can be erased/selected.
/// Base class for both DrawStroke and TextStroke.
/// </summary>
public abstract class PaintableStroke : Stroke, IClonable
{
    protected PaintableStroke() { }
    protected PaintableStroke(Guid id) : base(id) { }

    public bool IsToBeErased = false;
    public required HashSet<ToolOption> ToolOptions { get; init; } = [];
    public required StrokePaint Paint { get; init; } = new();

    public override void Dispose()
    {
        base.Dispose();
        Paint.DisposeSkPaint();
    }

    public abstract CanvasElement Clone(bool preserveId = false);
}