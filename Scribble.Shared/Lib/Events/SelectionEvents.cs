using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

public record CreateSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint StartPoint) : Event(ActionId);

public record IncreaseSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint Point) : Event(ActionId);

public record EndSelectionEvent(Guid ActionId, Guid BoundId) : Event(ActionId), ITerminalEvent;

public record ClearSelectionEvent(Guid ActionId) : Event(ActionId), ITerminalEvent;

/// <summary>
/// Selects a specific set of canvas elements by their IDs.
/// Unlike the other selection events, no spatial containment check is performed,
/// the targets are taken directly from <see cref="ElementIds"/>.
/// </summary>
public record SelectByIdsEvent(
    Guid ActionId,
    Guid BoundId,
    IReadOnlyList<Guid> ElementIds)
    : Event(ActionId), ITerminalEvent;