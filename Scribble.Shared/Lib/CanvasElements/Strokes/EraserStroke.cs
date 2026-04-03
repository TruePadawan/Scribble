namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a transient eraser stroke
/// </summary>
public class EraserStroke : Stroke
{
    /// <summary>
    /// The collection of canvas element IDs that are targets for erasure
    /// </summary>
    public HashSet<Guid> Targets = [];
}