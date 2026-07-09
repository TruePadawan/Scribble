using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

public record StartEraseStrokeEvent(Guid ActionId, Guid StrokeId, SKPoint StartPoint) : Event(ActionId);

public record EraseStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : Event(ActionId);

public record TriggerEraseEvent(Guid ActionId, Guid StrokeId) : Event(ActionId), ITerminalEvent;