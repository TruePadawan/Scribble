using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

public record CreateSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint StartPoint) : Event(ActionId);

public record IncreaseSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint Point) : Event(ActionId);

public record EndSelectionEvent(Guid ActionId, Guid BoundId) : Event(ActionId), ITerminalEvent;

public record ClearSelectionEvent(Guid ActionId) : Event(ActionId), ITerminalEvent;