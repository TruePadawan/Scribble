using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

public record MoveCanvasElementsEvent(Guid ActionId, Guid BoundId, SKPoint Delta) : Event(ActionId);

public record RotateCanvasElementsEvent(Guid ActionId, Guid BoundId, float DegreesRad, SKPoint Center)
    : Event(ActionId);

/// <summary>
/// Scales selected canvas elements by the given factor around the pivot (Center).
/// RotationRad carries the element's current rotation so the handler can scale
/// along the element's local axes instead of the world axes.
/// </summary>
public record ScaleCanvasElementsEvent(
    Guid ActionId, Guid BoundId, SKPoint Scale, SKPoint Center, float RotationRad = 0f) : Event(ActionId);