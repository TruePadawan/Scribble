using System.Text.Json.Serialization;

namespace Scribble.Shared.Lib.CanvasElements;

[JsonDerivedType(typeof(CanvasImage), typeDiscriminator: "CanvasImage")]
public abstract class CanvasElement
{
    public Guid Id { get; init; } = Guid.NewGuid();

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
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}