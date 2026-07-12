using System.Text.Json.Serialization;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements;

[JsonDerivedType(typeof(CanvasImage), typeDiscriminator: "CanvasImage")]
[JsonDerivedType(typeof(DrawStroke), typeDiscriminator: "DrawStroke")]
[JsonDerivedType(typeof(TextStroke), typeDiscriminator: "TextStroke")]
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
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
}

public interface IClonable
{
    /// <summary>
    /// Returns a deep copy of this element.
    /// </summary>
    CanvasElement Clone(bool preserveId = false);
}

public interface ISelectable
{
    /// <summary>
    /// The bounds of the element.
    /// </summary>
    public SKRect Bounds { get; }

    /// <summary>
    /// The rotation of the element in radians.
    /// </summary>
    public float Rotation { get; set; }
}