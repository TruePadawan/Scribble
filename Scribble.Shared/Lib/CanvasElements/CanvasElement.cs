using System.Text.Json.Serialization;
using Scribble.Shared.Lib.CanvasElements.Strokes;

namespace Scribble.Shared.Lib.CanvasElements;

[JsonDerivedType(typeof(CanvasImage), typeDiscriminator: "CanvasImage")]
[JsonDerivedType(typeof(DrawStroke), typeDiscriminator: "DrawStroke")]
[JsonDerivedType(typeof(TextStroke), typeDiscriminator: "TextStroke")]
public abstract class CanvasElement : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();

    // For JSON serialization/deserialization compatibility
    protected CanvasElement()
    {
    }

    protected CanvasElement(Guid id) => Id = id;

    /// <summary>
    /// Logical z-order layer index for this element. 0 is the base layer.
    /// </summary>
    public int LayerIndex { get; set; }

    /// <summary>
    /// The SignalR ConnectionId of the client that created this element.
    /// Null when the element was created in solo mode or loaded from a saved file.
    /// </summary>
    public string? CreatorConnectionId { get; set; }

    /// <summary>
    /// The time this element was created.
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public virtual void Dispose()
    {
    }
}

public interface IClonable
{
    /// <summary>
    /// Returns a deep copy of this element.
    /// </summary>
    CanvasElement Clone(bool preserveId = false);
}