using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a transient selection bound created by the Select Tool
/// </summary>
public class SelectionBound(Guid id) : Stroke(id)
{
    /// <summary>
    /// The collection of canvas element IDs that are selected
    /// </summary>
    public HashSet<Guid> Targets = [];

    public SelectionBound Clone(bool preserveId = false)
    {
        var clone = new SelectionBound(preserveId ? Id : Guid.NewGuid())
        {
            Path = new SKPath(Path),
            Targets = [..Targets],
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId
        };
        return clone;
    }
}