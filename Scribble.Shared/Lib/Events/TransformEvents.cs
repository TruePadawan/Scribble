using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

public record MoveCanvasElementsEvent(Guid ActionId, Guid BoundId, SKPoint Delta) : Event(ActionId);

public record RotateCanvasElementsEvent(Guid ActionId, Guid BoundId, float DegreesRad, SKPoint Center)
    : Event(ActionId);

public record ScaleCanvasElementsEvent(Guid ActionId, Guid BoundId, SKPoint Scale, SKPoint Center) : Event(ActionId);