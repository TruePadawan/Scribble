using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a transient eraser stroke
/// </summary>
public class EraserStroke(Guid id) : Stroke(id)
{
    /// <summary>
    /// The collection of canvas element IDs that are targets for erasure
    /// </summary>
    public HashSet<Guid> Targets = [];

    public EraserStroke Clone(bool preserveId = false)
    {
        var clone = new EraserStroke(preserveId ? Id : Guid.NewGuid())
        {
            Path = new SKPath(Path),
            Targets = [..Targets],
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId
        };
        return clone;
    }
}