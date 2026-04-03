namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a transient selection bound created by the Select Tool
/// </summary>
public class SelectionBound : Stroke
{
    /// <summary>
    /// The collection of canvas element IDs that are selected
    /// </summary>
    public HashSet<Guid> Targets = [];
}